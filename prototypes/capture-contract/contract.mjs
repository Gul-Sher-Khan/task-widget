// PROTOTYPE — the Capture → Tasks contract under test (issue #15).
// The liftable part: schema, instructions, tolerant parse, rule checks. No I/O here.

export const CONTRACT_VERSION = "v2";

// Strict json_schema: every property required, no additionalProperties.
// "No actionable Task" is an empty `tasks` array. Array order = spoken order.
export const SCHEMA = {
  name: "capture_tasks",
  strict: true,
  schema: {
    type: "object",
    additionalProperties: false,
    required: ["tasks"],
    properties: {
      tasks: {
        type: "array",
        items: {
          type: "object",
          additionalProperties: false,
          required: ["title", "details", "priority", "effort"],
          properties: {
            title: { type: "string" },
            details: { type: "string" },
            priority: { type: "string", enum: ["high", "medium", "low"] },
            effort: { type: "string", enum: ["quick", "short", "long"] },
          },
        },
      },
    },
  },
};

const WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

export function formatToday(d) {
  return `${WEEKDAYS[d.getDay()]} ${d.getDate()} ${MONTHS[d.getMonth()]} ${d.getFullYear()}`;
}

export function instructions(today) {
  return `You turn one dictated note (a "Capture") into tasks for the user's personal to-do list.

The Capture was dictated, so it may contain filler, false starts, self-corrections and transcription slips. When the user corrects themselves ("Tuesday, no, Thursday"), keep only the correction. Ignore filler.

Splitting
- Make one task per action the user would tick off on its own.
- Never break an action into steps or sub-tasks, even if the user lists the steps. Put steps they said into details instead.
- One action with several objects stays one task ("Buy milk, eggs and bread").
- Thinking, deciding or checking something counts as an action ("Decide whether to move to Postgres").
- Drop chatter, feelings, and facts that ask for no action. If nothing in the Capture asks for action, return an empty tasks list.
- Keep the tasks in the order they were said.

title
- One line, at most 60 characters, starting with a verb in the imperative ("Email Sarah the invoice").
- Use the Capture's own words wherever you can. Add nothing that the Capture doesn't say.
- Write it in the language of the Capture. Sentence case, no full stop at the end.

details
- Only context stated in the Capture that the title leaves out: who, deadlines, where, why, amounts, steps the user listed.
- Never invent steps, advice or anything else the user didn't say. If there is nothing to add, use "".
- Turn relative dates into a short weekday and date, using today's date: "before Friday" becomes "Before Fri 9 Oct". No year unless it isn't this year. Times as the user said them ("3pm", "before 9").
- Write details as one short phrase or sentence fragment, with no full stop at the end.

priority
- high: only when the Capture itself gives a reason: a deadline within 3 days of today, someone waiting on or blocked by the user, or a stated bad consequence (money, health, legal, something breaking).
- medium: it matters, but nothing breaks this week. A deadline more than 3 days away is medium. This is the default: an ordinary errand, email or chore with no reason given is medium.
- low: nice to do; nothing happens if it slips ("someday", "at some point", "if I get time").
- Words the user says about urgency ("urgent", "no rush", "whenever") win over your own judgement.

effort (how long the action itself takes)
- quick: 15 minutes or less (a call, a short message, a purchase on the way).
- short: up to an hour.
- long: more than an hour (writing, building, refactoring, studying).

Today is ${formatToday(today)}.`;
}

// Tolerant parse: plain JSON, else strip code fences, else the first {...} object.
export function parse(text) {
  const attempts = [text, text.replace(/^\s*```(?:json)?\s*|\s*```\s*$/g, "")];
  const start = text.indexOf("{"), end = text.lastIndexOf("}");
  if (start >= 0 && end > start) attempts.push(text.slice(start, end + 1));
  for (const a of attempts) {
    try {
      const v = JSON.parse(a);
      if (v && Array.isArray(v.tasks)) return { ok: true, tasks: v.tasks };
    } catch {}
  }
  return { ok: false, error: "Couldn't understand the reply" };
}

// Mechanical rule checks. Judgement calls (splitting, invented details) are for a human.
export function check(task) {
  const issues = [];
  if (task.title.length > 60) issues.push(`title ${task.title.length} chars`);
  if (/\n/.test(task.title)) issues.push("title has a line break");
  if (/\.$/.test(task.title.trim())) issues.push("title ends with a full stop");
  if (!["high", "medium", "low"].includes(task.priority)) issues.push(`bad priority ${task.priority}`);
  if (!["quick", "short", "long"].includes(task.effort)) issues.push(`bad effort ${task.effort}`);
  if (/^\s*(\d+[.)]|[-*•])\s/m.test(task.details)) issues.push("details is a list (steps?)");
  return issues;
}
