// PROTOTYPE: in-memory Task model for the look prototype. No persistence, no LLM: a Capture is "interpreted"
// by a few keyword rules after a fake delay so the processing states can be seen.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace Look;

public enum Pri { High, Medium, Low }
public enum Eff { Quick, Short, Long }

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TaskVm : Bindable
{
    static int seq;
    public int Id { get; } = ++seq;
    public DateTime Created { get; set; } = DateTime.Now;
    public string Capture { get; set; }

    string title, details;
    Pri priority;
    Eff effort;
    bool expanded, striking, editing, first, pending, done;

    public string Title { get => title; set => Set(ref title, value); }
    public string Details { get => details; set { if (Set(ref details, value)) Raise(nameof(HasDetails)); } }
    public bool HasDetails => !string.IsNullOrWhiteSpace(details);
    public Pri Priority { get => priority; set => Set(ref priority, value); }
    public Eff Effort { get => effort; set => Set(ref effort, value); }
    public bool IsExpanded { get => expanded; set => Set(ref expanded, value); }
    public bool IsStriking { get => striking; set => Set(ref striking, value); }
    public bool IsEditing { get => editing; set => Set(ref editing, value); }
    public bool IsFirst { get => first; set => Set(ref first, value); }
    public bool IsPending { get => pending; set { if (Set(ref pending, value)) RaiseKind(); } }
    public bool IsDone { get => done; set => Set(ref done, value); }

    public int Rank => (int)Priority * 10 + (int)Effort;

    // ---- Round 4: a Capture row that isn't a Task (yet): pending, waiting, failed, or its text being edited ----
    bool waiting, failed, editingCapture, dimmed;
    string reason = "", reasonTip, pendingText = "Interpreting…";

    public bool IsWaiting { get => waiting; set { if (Set(ref waiting, value)) RaiseKind(); } }
    public bool IsFailed { get => failed; set { if (Set(ref failed, value)) RaiseKind(); } }
    public bool IsEditingCapture { get => editingCapture; set { if (Set(ref editingCapture, value)) RaiseKind(); } }
    // A waiting or failed row, or a Capture's text open for editing: drawn by CaptureRow instead of the Task row.
    public bool IsCaptureRow => waiting || failed || editingCapture;
    public bool IsTaskRow => !IsCaptureRow;
    // Anything that isn't a real Task: never counted, ranked, ticked or dragged.
    public bool NotTask => pending || IsCaptureRow;
    public bool ShowFailed => failed && !editingCapture;
    public bool ShowWaiting => waiting && !editingCapture;
    void RaiseKind()
    {
        foreach (var n in new[] { nameof(IsCaptureRow), nameof(IsTaskRow), nameof(NotTask), nameof(ShowFailed), nameof(ShowWaiting) }) Raise(n);
    }

    public string Reason { get => reason; set => Set(ref reason, value); }
    public string ReasonTip { get => reasonTip; set => Set(ref reasonTip, value); }
    public string PendingText { get => pendingText; set => Set(ref pendingText, value); }
    // Waiting rows light the attention dot unless they're only waiting for the network.
    public bool WaitDot { get; set; }
    // "No task found in this": Make a Task as-is is the primary action instead of Retry.
    bool makeTaskFirst;
    public bool MakeTaskFirst { get => makeTaskFirst; set { if (Set(ref makeTaskFirst, value)) Raise(nameof(RetryFirst)); } }
    public bool RetryFirst => !makeTaskFirst;
    // Re-interpret: the Capture's old not-Done Tasks, dimmed while it runs and replaced when it succeeds.
    public List<TaskVm> ReinterpretOf { get; set; }
    public bool IsDimmed { get => dimmed; set => Set(ref dimmed, value); }
}

public sealed class Store : Bindable
{
    public ObservableCollection<TaskVm> Open { get; } = new();
    public ObservableCollection<TaskVm> Done { get; } = new();

    bool manual, showDone, attention, undoVisible;
    int processing;
    string undoText = "";

    public bool Manual { get => manual; set => Set(ref manual, value); }
    public bool ShowDone { get => showDone; set { if (Set(ref showDone, value)) { Raise(nameof(Visible)); Raise(nameof(ShowingOpen)); } } }
    public bool ShowingOpen => !showDone;
    public ObservableCollection<TaskVm> Visible => showDone ? Done : Open;
    // Lit by hand (control bar, updates) or while any failed row, or a waiting row that needs the user, exists.
    public bool Attention { get => attention || Open.Any(t => t.IsFailed || (t.IsWaiting && t.WaitDot)); set { attention = value; Raise(nameof(Attention)); } }
    public void RecomputeAttention() => Raise(nameof(Attention));
    public int Processing { get => processing; set { if (Set(ref processing, value)) Raise(nameof(IsProcessing)); } }
    public bool IsProcessing => processing > 0;
    public bool UndoVisible { get => undoVisible; set => Set(ref undoVisible, value); }
    public string UndoText { get => undoText; set => Set(ref undoText, value); }

    public int CountHigh => Open.Count(t => !t.NotTask && t.Priority == Pri.High);
    public int CountMedium => Open.Count(t => !t.NotTask && t.Priority == Pri.Medium);
    public int CountLow => Open.Count(t => !t.NotTask && t.Priority == Pri.Low);
    public int CountOpen => Open.Count(t => !t.NotTask);
    public bool AllClear => CountOpen == 0;
    // Prototype simplification: every Done Task counts as done today.
    public int DoneToday => Done.Count;
    public double DayProgress => DoneToday + CountOpen == 0 ? 0 : (double)DoneToday / (DoneToday + CountOpen);
    public TaskVm Top => Open.FirstOrDefault(t => !t.NotTask);
    public int OthersCount => System.Math.Max(0, CountOpen - 1);

    public Store()
    {
        Open.CollectionChanged += (_, _) => Refresh();
        Done.CollectionChanged += (_, _) => { foreach (var t in Done) t.IsFirst = false; Raise(nameof(Done)); Raise(nameof(DoneToday)); Raise(nameof(DayProgress)); };
    }

    void Refresh()
    {
        var firstReal = Open.FirstOrDefault(t => !t.NotTask);
        foreach (var t in Open) t.IsFirst = t == firstReal;
        Raise(nameof(CountHigh)); Raise(nameof(CountMedium)); Raise(nameof(CountLow));
        Raise(nameof(CountOpen)); Raise(nameof(AllClear)); Raise(nameof(DayProgress)); Raise(nameof(Top)); Raise(nameof(OthersCount));
        Raise(nameof(IsEmpty)); RecomputeAttention();
    }

    // Nothing at all in the list (no Tasks and no Capture rows): the empty state shows.
    public bool IsEmpty => Open.Count == 0;

    // ---- ranking & insertion ----
    static int Compare(TaskVm a, TaskVm b)
    {
        int c = a.Rank.CompareTo(b.Rank);
        return c != 0 ? c : a.Created.CompareTo(b.Created);
    }

    // Rule-based insertion: before the first Task that ranks after it, leaving manual positions alone.
    int InsertIndex(TaskVm t)
    {
        int start = Open.TakeWhile(x => x.NotTask).Count();
        for (int i = start; i < Open.Count; i++)
            if (!Open[i].NotTask && Compare(t, Open[i]) < 0) return i;
        return Open.Count;
    }

    void Place(TaskVm t) => Open.Insert(InsertIndex(t), t);

    public void Resort()
    {
        if (P.ReadOnly) return; // saved by a newer version
        var sorted = Open.Where(t => !t.NotTask).OrderBy(t => t, Comparer<TaskVm>.Create(Compare)).ToList();
        var before = Open.ToList();
        Apply(sorted);
        Manual = false;
        Push("Re-sort", () => { Apply(before); Manual = true; }, () => { Apply(sorted); Manual = false; });
    }

    void Apply(List<TaskVm> order)
    {
        var pending = Open.Where(t => t.NotTask).ToList();
        var target = pending.Concat(order.Where(t => !t.NotTask && Open.Contains(t))).ToList();
        for (int i = 0; i < target.Count; i++)
        {
            int cur = Open.IndexOf(target[i]);
            if (cur != i) Open.Move(cur, i);
        }
    }

    // ---- actions ----
    public void ToggleExpand(TaskVm t)
    {
        if (!t.HasDetails) return;
        bool open = !t.IsExpanded;
        foreach (var x in Open.Concat(Done)) x.IsExpanded = false;
        t.IsExpanded = open;
    }

    public void ToggleComplete(TaskVm t)
    {
        if (P.ReadOnly) return; // saved by a newer version
        if (t.NotTask) return;
        if (t.IsDone) { Uncomplete(t); return; }
        if (t.IsStriking) { t.IsStriking = false; return; } // second tick within the window cancels
        t.IsStriking = true;
        Shell.After((int)Shell.Ms("StrikeHoldMs"), () =>
        {
            if (!t.IsStriking || !Open.Contains(t)) return;
            int idx = Open.IndexOf(t);
            Open.Remove(t);
            t.IsStriking = false; t.IsExpanded = false; t.IsDone = true;
            Done.Insert(0, t);
            Push("Task done", () => { Done.Remove(t); t.IsDone = false; Open.Insert(Math.Min(idx, Open.Count), t); },
                              () => { Open.Remove(t); t.IsDone = true; Done.Insert(0, t); }, pill: true);
        });
    }

    void Uncomplete(TaskVm t)
    {
        Done.Remove(t);
        t.IsDone = false;
        Place(t);
        Push("Task returned", () => { Open.Remove(t); t.IsDone = true; Done.Insert(0, t); },
                              () => { Done.Remove(t); t.IsDone = false; Place(t); });
    }

    public void Delete(TaskVm t)
    {
        if (P.ReadOnly) return; // saved by a newer version
        var list = t.IsDone ? Done : Open;
        int idx = list.IndexOf(t);
        if (idx < 0) return;
        list.Remove(t);
        Push("Task deleted", () => list.Insert(Math.Min(idx, list.Count), t), () => list.Remove(t), pill: true);
    }

    public void CyclePriority(TaskVm t)
    {
        if (P.ReadOnly) return; // saved by a newer version
        var old = t.Priority;
        t.Priority = (Pri)(((int)old + 1) % 3);
        Replace(t, () => t.Priority = old, () => t.Priority = (Pri)(((int)old + 1) % 3));
    }

    public void CycleEffort(TaskVm t)
    {
        if (P.ReadOnly) return; // saved by a newer version
        var old = t.Effort;
        t.Effort = (Eff)(((int)old + 1) % 3);
        Replace(t, () => t.Effort = old, () => t.Effort = (Eff)(((int)old + 1) % 3));
    }

    public void Rename(TaskVm t, string title)
    {
        if (P.ReadOnly) return; // saved by a newer version
        var old = t.Title;
        if (string.IsNullOrWhiteSpace(title) || title == old) return;
        t.Title = title.Trim();
        Push("Edit", () => t.Title = old, () => t.Title = title.Trim());
    }

    // An edited Task is re-placed by the insertion rule.
    void Replace(TaskVm t, Action undoProp, Action redoProp)
    {
        if (t.IsDone) { Push("Edit", undoProp, redoProp); return; }
        int idx = Open.IndexOf(t);
        Open.Remove(t);
        Place(t);
        int now = Open.IndexOf(t);
        Push("Edit", () => { undoProp(); Open.Move(Open.IndexOf(t), Math.Min(idx, Open.Count - 1)); },
                     () => { redoProp(); Open.Move(Open.IndexOf(t), Math.Min(now, Open.Count - 1)); });
    }

    public void Move(TaskVm t, int delta)
    {
        if (P.ReadOnly) return; // saved by a newer version
        int i = Open.IndexOf(t), j = i + delta;
        if (i < 0 || j < 0 || j >= Open.Count || Open[j].NotTask) return;
        Open.Move(i, j);
        bool wasManual = Manual;
        Manual = true;
        Push("Move", () => { Open.Move(Open.IndexOf(t), i); Manual = wasManual; }, () => { Open.Move(Open.IndexOf(t), j); Manual = true; });
    }

    public void DragMoved(TaskVm t, int from)
    {
        if (P.ReadOnly) return; // saved by a newer version
        int to = Open.IndexOf(t);
        if (to == from) return;
        bool wasManual = Manual;
        Manual = true;
        Push("Move", () => { Open.Move(Open.IndexOf(t), from); Manual = wasManual; }, () => { Open.Move(Open.IndexOf(t), to); Manual = true; });
    }

    // ---- Capture ----
    // Round 4: a Capture row runs now (pending), waits (offline, signed out, no Connection yet) or fails into a failed row.
    // What a run returns is set on the control bar ("Next Capture"); "Hold pending" freezes pending rows for a look.
    public void Capture(string text)
    {
        if (P.ReadOnly) return; // saved by a newer version
        text = text.Trim();
        if (text.Length == 0) return;
        var row = new TaskVm { Title = text, Capture = text };
        Open.Insert(0, row);
        Run(row);
    }

    static Prefs P => Shell.Prefs;
    readonly List<Action> held = new();

    public void Run(TaskVm row)
    {
        row.IsEditingCapture = false;
        if (P.SignedOut) { Wait(row, P.Welcomed ? "Signed out" : "Connect your ChatGPT account", dot: true); return; }
        if (P.Offline) { Wait(row, "Waiting for connection", dot: false); return; }
        row.IsWaiting = false;
        row.IsFailed = false;
        row.PendingText = row.ReinterpretOf != null ? "Re-interpreting…" : "Interpreting…";
        row.IsPending = true;
        Dim(row, true);
        Processing++;
        int outcome = P.CaptureOutcome;
        int delay = (1800 + Random.Shared.Next(1200)) * (P.SlowMo ? 5 : 1);
        Shell.After(delay, () =>
        {
            void Finish()
            {
                Processing--;
                if (!Open.Contains(row)) return;
                var made = outcome == 0 ? Interpret(row.Title) : null;
                if (made is { Count: 0 }) outcome = 4;
                if (outcome != 0) { Fail(row, outcome); return; }
                Open.Remove(row);
                var old = row.ReinterpretOf?.Where(Open.Contains).ToList() ?? new();
                foreach (var o in old) { o.IsDimmed = false; Open.Remove(o); }
                foreach (var t in made) Place(t);
                Push(old.Count > 0 ? "Re-interpret" : "Capture",
                     () => { foreach (var t in made) Open.Remove(t); foreach (var o in old) Place(o); },
                     () => { foreach (var o in old) Open.Remove(o); foreach (var t in made) Place(t); });
            }
            if (P.HoldPending) held.Add(Finish); else Finish();
        });
    }

    public void ReleaseHeld()
    {
        var all = held.ToList();
        held.Clear();
        foreach (var a in all) a();
    }

    void Wait(TaskVm row, string reason, bool dot)
    {
        row.IsPending = false;
        row.IsFailed = false;
        row.Reason = reason;
        row.ReasonTip = null;
        row.WaitDot = dot;
        row.IsWaiting = true;
        RecomputeAttention();
    }

    // Reasons from "Capture flow and failure handling" (amended for ChatGPT only); the tooltip carries the detail.
    void Fail(TaskVm row, int outcome)
    {
        Dim(row, false);
        row.IsPending = false;
        row.Reason = row.ReinterpretOf != null ? "Re-interpret failed" : outcome switch
        {
            1 => "Couldn't reach ChatGPT",
            2 => "Rate-limited by ChatGPT",
            3 => "Couldn't understand the reply",
            4 => "No task found in this",
            _ => "Your ChatGPT plan can't be used here",
        };
        row.ReasonTip = outcome switch
        {
            1 => "No reply within 30 seconds, after one retry.",
            2 => "ChatGPT returned 429 Too Many Requests. Try again in a minute.",
            3 => "The reply didn't match the Task format, after one retry.",
            4 => "ChatGPT found nothing to do in this Capture.",
            _ => "ChatGPT returned 403: this plan isn't eligible. Go, Plus or Pro works.",
        };
        row.MakeTaskFirst = outcome == 4;
        row.IsFailed = true;
        RecomputeAttention();
    }

    void Dim(TaskVm row, bool on)
    {
        if (row.ReinterpretOf == null) return;
        foreach (var o in row.ReinterpretOf) o.IsDimmed = on && Open.Contains(o);
    }

    // Every waiting row runs once the reason it waited for is gone (back online, signed in).
    public void RunWaiting()
    {
        foreach (var row in Open.Where(t => t.IsWaiting).ToList()) Run(row);
    }

    public void Retry(TaskVm row) { if (!P.ReadOnly) Run(row); }

    public void EditCapture(TaskVm row) { if (!P.ReadOnly) row.IsEditingCapture = true; }

    public void SubmitCapture(TaskVm row, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        row.Title = text;
        row.Capture = text;
        Run(row);
    }

    public void CancelCaptureEdit(TaskVm row)
    {
        if (row.IsFailed || row.IsWaiting) row.IsEditingCapture = false;
        else Open.Remove(row); // a Re-interpret that never ran
    }

    public void MakeAsIs(TaskVm row)
    {
        if (P.ReadOnly) return; // saved by a newer version
        int idx = Open.IndexOf(row);
        Open.Remove(row);
        string title = row.Title.Split('\n')[0].Trim();
        var t = new TaskVm { Title = title, Priority = Pri.Medium, Effort = Eff.Short, Capture = row.Title };
        Place(t);
        Push("Task added", () => { Open.Remove(t); Open.Insert(Math.Min(idx, Open.Count), row); },
                           () => { Open.Remove(row); Place(t); });
    }

    public void Discard(TaskVm row)
    {
        if (P.ReadOnly) return; // saved by a newer version
        int idx = Open.IndexOf(row);
        Open.Remove(row);
        Dim(row, false);
        Push("Capture discarded", () => Open.Insert(Math.Min(idx, Open.Count), row), () => Open.Remove(row), pill: true);
    }

    // Re-interpret: open the Capture's text for fixing; its not-Done Tasks are replaced when the run succeeds.
    public void Reinterpret(TaskVm t)
    {
        if (P.ReadOnly) return; // saved by a newer version
        var siblings = Open.Where(x => !x.NotTask && x.Capture == t.Capture).ToList();
        if (siblings.Count == 0) siblings.Add(t);
        var row = new TaskVm { Title = t.Capture ?? t.Title, Capture = t.Capture, ReinterpretOf = siblings, IsEditingCapture = true };
        Open.Insert(0, row);
    }

    // Prototype scene: every Capture state at once, for side-by-side review.
    public void SeedCaptureStates()
    {
        Seed(5);
        void Add(TaskVm r) => Open.Insert(0, r);
        var w = new TaskVm { Title = "Ask Hamza for the staging credentials", Capture = "Ask Hamza for the staging credentials" };
        Add(w); Wait(w, "Waiting for connection", dot: false);
        var z = new TaskVm { Title = "Thursday was a good day honestly", Capture = "Thursday was a good day honestly" };
        Add(z); Fail(z, 4);
        var f = new TaskVm { Title = "Renew the parking permit before the 15th, the office one not home", Capture = "x" };
        f.Capture = f.Title;
        Add(f); Fail(f, 1);
        const string cap = "Email Bilal the signed contract, he's waiting on it today and also plan the offsite agenda";
        foreach (var t in Interpret(cap)) Place(t);
        var any = Open.First(t => t.Capture == cap);
        Reinterpret(any);
        P.HoldPending = true; // the scene opens frozen so the Re-interpret can be looked at; the bar releases it
        SubmitCapture(Open[0], cap.Replace("offsite agenda", "offsite agenda for March"));
    }

    static List<TaskVm> Interpret(string text)
    {
        var parts = text.Split(new[] { " and then ", ". ", ";", "\n", " and also " }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim().TrimEnd('.')).Where(p => p.Length > 2).ToList();
        var result = new List<TaskVm>();
        foreach (var p in parts)
        {
            string lower = p.ToLowerInvariant();
            var pri = lower.Contains("urgent") || lower.Contains("asap") || lower.Contains("today") || lower.Contains("waiting") ? Pri.High
                    : lower.Contains("maybe") || lower.Contains("someday") || lower.Contains("eventually") ? Pri.Low : Pri.Medium;
            var eff = lower.StartsWith("call") || lower.StartsWith("reply") || lower.StartsWith("email") || lower.StartsWith("send") || lower.StartsWith("text") ? Eff.Quick
                    : lower.StartsWith("plan") || lower.StartsWith("build") || lower.StartsWith("design") || lower.StartsWith("migrate") ? Eff.Long : Eff.Short;
            string title = p, details = null;
            int cut = p.IndexOfAny(new[] { ',', '—', '(' });
            if (cut > 12) { title = p[..cut].Trim(); details = char.ToUpper(p[(cut + 1)..].Trim()[0]) + p[(cut + 1)..].Trim()[1..].TrimEnd(')'); }
            title = char.ToUpper(title[0]) + title[1..];
            result.Add(new TaskVm { Title = title, Details = details, Priority = pri, Effort = eff, Capture = text });
        }
        return result;
    }

    // ---- undo ----
    readonly Stack<(Action undo, Action redo)> undo = new(), redo = new();
    int pillToken;

    void Push(string what, Action u, Action r, bool pill = false)
    {
        undo.Push((u, r));
        redo.Clear();
        if (pill) ShowPill(what);
    }

    void ShowPill(string what)
    {
        UndoText = what;
        UndoVisible = true;
        int token = ++pillToken;
        Shell.After((int)Shell.Ms("UndoStayMs"), () => { if (token == pillToken) UndoVisible = false; });
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        var a = undo.Pop();
        a.undo();
        redo.Push(a);
        UndoVisible = false;
    }

    public void Redo()
    {
        if (redo.Count == 0) return;
        var a = redo.Pop();
        a.redo();
        undo.Push(a);
    }

    // ---- seed ----
    public void Seed(int n)
    {
        Open.Clear(); Done.Clear(); undo.Clear(); redo.Clear();
        Manual = false; UndoVisible = false;
        var now = DateTime.Now;
        var all = new (string t, string d, Pri p, Eff e)[]
        {
            ("Reply to Sana about the Q3 budget", "She needs the revised numbers before Thursday's finance sync; the travel line is the open question.", Pri.High, Eff.Quick),
            ("Fix the flaky login test on CI", "Fails about 1 in 5 runs on the Windows agent since the token refresh change.", Pri.High, Eff.Short),
            ("Send the invoice to Northwind", null, Pri.High, Eff.Quick),
            ("Review Ali's PR on the caching layer", "Ali asked for eyes on the eviction policy specifically.", Pri.Medium, Eff.Short),
            ("Book the dentist appointment", null, Pri.Medium, Eff.Quick),
            ("Write release notes for 0.4", "Mention the new Dock and the Ctrl+Shift tap.", Pri.Medium, Eff.Short),
            ("Plan Friday's demo", "Ten minutes, for the whole team, live rather than slides.", Pri.Medium, Eff.Long),
            ("Renew the domain before it lapses", "Expires on the 28th.", Pri.Medium, Eff.Quick),
            ("Clean up the Downloads folder", null, Pri.Low, Eff.Short),
            ("Read the WebView2 memory article", "The one Hamza shared in #frontend.", Pri.Low, Eff.Short),
            ("Update the roadmap slide", null, Pri.Low, Eff.Quick),
            ("Try the new Wispr Flow commands", null, Pri.Low, Eff.Long),
            ("Call the bank about the card limit", null, Pri.High, Eff.Quick),
            ("Draft the onboarding email for Maryam", "She starts on Monday; include the laptop pickup time.", Pri.Medium, Eff.Short),
            ("Migrate the notes repo to the new laptop", null, Pri.Low, Eff.Long),
        };
        var picked = all.Take(Math.Min(n, all.Length)).Select((x, i) => new TaskVm
        {
            Title = x.t, Details = x.d, Priority = x.p, Effort = x.e, Created = now.AddMinutes(-200 + i * 7),
            Capture = x.t + (x.d != null ? ", " + x.d : ""),
        }).OrderBy(t => t, Comparer<TaskVm>.Create(Compare));
        foreach (var t in picked) Open.Add(t);
        if (n > 0) Done.Add(new TaskVm { Title = "Pay the electricity bill", Priority = Pri.High, Effort = Eff.Quick, IsDone = true });
        if (n > 0) Done.Add(new TaskVm { Title = "Order a new keyboard", Details = "The Keychron with brown switches.", Priority = Pri.Low, Effort = Eff.Quick, IsDone = true });
        Refresh();
    }
}
