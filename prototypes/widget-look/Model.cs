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
    public bool IsPending { get => pending; set => Set(ref pending, value); }
    public bool IsDone { get => done; set => Set(ref done, value); }

    public int Rank => (int)Priority * 10 + (int)Effort;
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
    public bool Attention { get => attention; set => Set(ref attention, value); }
    public int Processing { get => processing; set { if (Set(ref processing, value)) Raise(nameof(IsProcessing)); } }
    public bool IsProcessing => processing > 0;
    public bool UndoVisible { get => undoVisible; set => Set(ref undoVisible, value); }
    public string UndoText { get => undoText; set => Set(ref undoText, value); }

    public int CountHigh => Open.Count(t => !t.IsPending && t.Priority == Pri.High);
    public int CountMedium => Open.Count(t => !t.IsPending && t.Priority == Pri.Medium);
    public int CountLow => Open.Count(t => !t.IsPending && t.Priority == Pri.Low);
    public int CountOpen => Open.Count(t => !t.IsPending);
    public bool AllClear => CountOpen == 0;
    // Prototype simplification: every Done Task counts as done today.
    public int DoneToday => Done.Count;
    public double DayProgress => DoneToday + CountOpen == 0 ? 0 : (double)DoneToday / (DoneToday + CountOpen);
    public TaskVm Top => Open.FirstOrDefault(t => !t.IsPending);
    public int OthersCount => System.Math.Max(0, CountOpen - 1);

    public Store()
    {
        Open.CollectionChanged += (_, _) => Refresh();
        Done.CollectionChanged += (_, _) => { foreach (var t in Done) t.IsFirst = false; Raise(nameof(Done)); Raise(nameof(DoneToday)); Raise(nameof(DayProgress)); };
    }

    void Refresh()
    {
        var firstReal = Open.FirstOrDefault(t => !t.IsPending);
        foreach (var t in Open) t.IsFirst = t == firstReal;
        Raise(nameof(CountHigh)); Raise(nameof(CountMedium)); Raise(nameof(CountLow));
        Raise(nameof(CountOpen)); Raise(nameof(AllClear)); Raise(nameof(DayProgress)); Raise(nameof(Top)); Raise(nameof(OthersCount));
    }

    // ---- ranking & insertion ----
    static int Compare(TaskVm a, TaskVm b)
    {
        int c = a.Rank.CompareTo(b.Rank);
        return c != 0 ? c : a.Created.CompareTo(b.Created);
    }

    // Rule-based insertion: before the first Task that ranks after it, leaving manual positions alone.
    int InsertIndex(TaskVm t)
    {
        int start = Open.TakeWhile(x => x.IsPending).Count();
        for (int i = start; i < Open.Count; i++)
            if (!Open[i].IsPending && Compare(t, Open[i]) < 0) return i;
        return Open.Count;
    }

    void Place(TaskVm t) => Open.Insert(InsertIndex(t), t);

    public void Resort()
    {
        var sorted = Open.Where(t => !t.IsPending).OrderBy(t => t, Comparer<TaskVm>.Create(Compare)).ToList();
        var before = Open.ToList();
        Apply(sorted);
        Manual = false;
        Push("Re-sort", () => { Apply(before); Manual = true; }, () => { Apply(sorted); Manual = false; });
    }

    void Apply(List<TaskVm> order)
    {
        var pending = Open.Where(t => t.IsPending).ToList();
        var target = pending.Concat(order.Where(t => !t.IsPending && Open.Contains(t))).ToList();
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
        if (t.IsPending) return;
        if (t.IsDone) { Uncomplete(t); return; }
        if (t.IsStriking) { t.IsStriking = false; return; } // second tick within the window cancels
        t.IsStriking = true;
        Shell.After(1500, () =>
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
        var list = t.IsDone ? Done : Open;
        int idx = list.IndexOf(t);
        if (idx < 0) return;
        list.Remove(t);
        Push("Task deleted", () => list.Insert(Math.Min(idx, list.Count), t), () => list.Remove(t), pill: true);
    }

    public void CyclePriority(TaskVm t)
    {
        var old = t.Priority;
        t.Priority = (Pri)(((int)old + 1) % 3);
        Replace(t, () => t.Priority = old, () => t.Priority = (Pri)(((int)old + 1) % 3));
    }

    public void CycleEffort(TaskVm t)
    {
        var old = t.Effort;
        t.Effort = (Eff)(((int)old + 1) % 3);
        Replace(t, () => t.Effort = old, () => t.Effort = (Eff)(((int)old + 1) % 3));
    }

    public void Rename(TaskVm t, string title)
    {
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
        int i = Open.IndexOf(t), j = i + delta;
        if (i < 0 || j < 0 || j >= Open.Count || Open[j].IsPending) return;
        Open.Move(i, j);
        bool wasManual = Manual;
        Manual = true;
        Push("Move", () => { Open.Move(Open.IndexOf(t), i); Manual = wasManual; }, () => { Open.Move(Open.IndexOf(t), j); Manual = true; });
    }

    public void DragMoved(TaskVm t, int from)
    {
        int to = Open.IndexOf(t);
        if (to == from) return;
        bool wasManual = Manual;
        Manual = true;
        Push("Move", () => { Open.Move(Open.IndexOf(t), from); Manual = wasManual; }, () => { Open.Move(Open.IndexOf(t), to); Manual = true; });
    }

    // ---- Capture ----
    public void Capture(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        var placeholder = new TaskVm { Title = text, IsPending = true };
        Open.Insert(0, placeholder);
        Processing++;
        Shell.After(1800 + Random.Shared.Next(1200), () =>
        {
            Processing--;
            Open.Remove(placeholder);
            var made = Interpret(text);
            if (made.Count == 0) { Attention = true; return; }
            foreach (var t in made) Place(t);
            Push("Capture", () => { foreach (var t in made) Open.Remove(t); }, () => { foreach (var t in made) Place(t); });
        });
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
        Shell.After(5000, () => { if (token == pillToken) UndoVisible = false; });
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
        Done.Add(new TaskVm { Title = "Pay the electricity bill", Priority = Pri.High, Effort = Eff.Quick, IsDone = true });
        Done.Add(new TaskVm { Title = "Order a new keyboard", Details = "The Keychron with brown switches.", Priority = Pri.Low, Effort = Eff.Quick, IsDone = true });
        Refresh();
    }
}
