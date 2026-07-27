// Build-less React: real React + ReactDOM loaded straight from a CDN, with
// htm for JSX-like template syntax. No npm, no bundler, no package.json -
// this page ships as a plain static file exactly like every other page in
// wwwroot, matching the app's existing zero-build-tooling approach.
import React from "https://esm.sh/react@18";
import ReactDOM from "https://esm.sh/react-dom@18/client";
import htm from "https://esm.sh/htm@3";

const html = htm.bind(React.createElement);
const { useState, useEffect, useCallback } = React;

function todayLocalDateString() {
  return dateToLocalString(new Date());
}

function daysAgoLocalDateString(n) {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return dateToLocalString(d);
}

function dateToLocalString(d) {
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

// Formats an API ISO datetime string for a <input type="datetime-local">
// value, using the browser's local time components - mirrors how the
// server treats every attendance timestamp as local (DateTime.Now).
function toDatetimeLocalValue(isoString) {
  const d = new Date(isoString);
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function formatDateTime(isoString) {
  return new Date(isoString).toLocaleString();
}

function formatDuration(totalMinutes) {
  const hours = Math.floor(totalMinutes / 60);
  const minutes = Math.round(totalMinutes % 60);
  return `${hours}h ${minutes}m`;
}

// A non-ok response body is one of two shapes: our own controller's
// BadRequest("some message")/NotFound() (plain text, already readable), or
// ASP.NET Core's automatic model-binding validation response (raw
// application/problem+json with a .NET type-conversion error under
// `errors`, e.g. if a date field couldn't be parsed) - that one needs
// translating rather than shown as-is.
async function extractErrorMessage(res) {
  const text = await res.text();
  if (!text) {
    return `Request failed (${res.status}).`;
  }

  try {
    const problem = JSON.parse(text);
    if (problem && typeof problem === "object" && problem.errors) {
      return "One or more fields have an invalid value. Please check the dates and try again.";
    }
    if (problem && typeof problem === "object" && problem.title) {
      return problem.title;
    }
  } catch {
    // Not JSON - one of our own plain-text BadRequest(...) messages.
  }

  return text;
}

function SessionRow({ name, session, index, onSave, onDelete }) {
  const [editing, setEditing] = useState(false);
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [error, setError] = useState("");

  function beginEdit() {
    setStart(toDatetimeLocalValue(session.start));
    setEnd(toDatetimeLocalValue(session.end));
    setError("");
    setEditing(true);
  }

  async function save() {
    const result = await onSave(name, index, start, end);
    if (result.ok) {
      setEditing(false);
    } else {
      setError(result.error);
    }
  }

  if (editing) {
    return html`
      <tr>
        <td>${name}</td>
        <td><input type="datetime-local" value=${start} onChange=${(e) => setStart(e.target.value)} /></td>
        <td><input type="datetime-local" value=${end} onChange=${(e) => setEnd(e.target.value)} /></td>
        <td>
          <button type="button" onClick=${save}>Save</button>
          <button type="button" onClick=${() => setEditing(false)}>Cancel</button>
          ${error ? html`<span class="edit-error">${error}</span>` : null}
        </td>
      </tr>
    `;
  }

  return html`
    <tr>
      <td>${name}</td>
      <td>${formatDateTime(session.start)}</td>
      <td>${formatDateTime(session.end)}</td>
      <td>
        <button type="button" onClick=${beginEdit}>Edit</button>
        <button type="button" onClick=${() => onDelete(name, index)}>Delete</button>
      </td>
    </tr>
  `;
}

function DailyCorrections() {
  const [date, setDate] = useState(todayLocalDateString());
  const [employees, setEmployees] = useState([]);
  const [status, setStatus] = useState("");
  const [people, setPeople] = useState([]);
  const [addName, setAddName] = useState("");
  const [addStart, setAddStart] = useState("");
  const [addEnd, setAddEnd] = useState("");
  const [addError, setAddError] = useState("");

  const load = useCallback(async () => {
    setStatus("Loading...");
    try {
      const res = await fetch(`/api/attendance/${date}`);
      if (!res.ok) {
        throw new Error(`Request failed: ${res.status}`);
      }
      const data = await res.json();
      setEmployees(data.employees);
      setStatus(data.employees.length === 0 ? "No attendance recorded for this day." : "");
    } catch (err) {
      setStatus(`Error: ${err.message}`);
    }
  }, [date]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    fetch("/api/people")
      .then((r) => r.json())
      .then((d) => setPeople(d.names || []))
      .catch(() => {});
  }, []);

  async function saveSession(name, index, start, end) {
    const res = await fetch(`/api/attendance/${date}/${encodeURIComponent(name)}/sessions/${index}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ start, end })
    });
    if (res.ok) {
      await load();
      return { ok: true };
    }
    return { ok: false, error: await extractErrorMessage(res) };
  }

  async function deleteSession(name, index) {
    if (!confirm(`Delete this session for ${name}?`)) {
      return;
    }
    const res = await fetch(`/api/attendance/${date}/${encodeURIComponent(name)}/sessions/${index}`, { method: "DELETE" });
    if (res.ok) {
      await load();
    } else {
      setStatus(`Error: ${await extractErrorMessage(res)}`);
    }
  }

  async function addSession(e) {
    e.preventDefault();
    if (!addName || !addStart || !addEnd) {
      return;
    }
    setAddError("");
    const res = await fetch(`/api/attendance/${date}/${encodeURIComponent(addName)}/sessions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ start: addStart, end: addEnd })
    });
    if (res.ok) {
      setAddStart("");
      setAddEnd("");
      await load();
    } else {
      setAddError(await extractErrorMessage(res));
    }
  }

  const rows = [];
  for (const employee of employees) {
    employee.sessions.forEach((session, index) => {
      rows.push(html`
        <${SessionRow}
          key=${`${employee.name}-${index}`}
          name=${employee.name}
          session=${session}
          index=${index}
          onSave=${saveSession}
          onDelete=${deleteSession}
        />
      `);
    });
  }

  return html`
    <section class="panel">
      <div class="camera-controls">
        <input type="date" value=${date} onChange=${(e) => setDate(e.target.value)} />
        <button type="button" onClick=${load}>Refresh</button>
      </div>
      <p class="status">${status}</p>
      <table class="people-table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Start</th>
            <th>End</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>${rows}</tbody>
      </table>

      <h3>Add a session</h3>
      <form class="enroll-form" onSubmit=${addSession}>
        <select value=${addName} onChange=${(e) => setAddName(e.target.value)}>
          <option value="">Select person...</option>
          ${people.map((n) => html`<option key=${n} value=${n}>${n}</option>`)}
        </select>
        <input type="datetime-local" value=${addStart} onChange=${(e) => setAddStart(e.target.value)} />
        <input type="datetime-local" value=${addEnd} onChange=${(e) => setAddEnd(e.target.value)} />
        <button type="submit">Add session</button>
        ${addError ? html`<span class="edit-error">${addError}</span>` : null}
      </form>
    </section>
  `;
}

function RangeReport() {
  const [start, setStart] = useState(daysAgoLocalDateString(6));
  const [end, setEnd] = useState(todayLocalDateString());
  const [employees, setEmployees] = useState([]);
  const [status, setStatus] = useState("");

  const load = useCallback(async () => {
    setStatus("Loading...");
    try {
      const res = await fetch(`/api/attendance/range?start=${start}&end=${end}`);
      if (!res.ok) {
        throw new Error(await extractErrorMessage(res));
      }
      const data = await res.json();
      setEmployees(data.employees);
      setStatus(data.employees.length === 0 ? "No attendance recorded for this range." : "");
    } catch (err) {
      setStatus(`Error: ${err.message}`);
    }
  }, [start, end]);

  useEffect(() => {
    load();
  }, [load]);

  function exportCsv() {
    window.location.href = `/api/attendance/range/export?start=${start}&end=${end}`;
  }

  return html`
    <section class="panel">
      <div class="camera-controls">
        <input type="date" value=${start} onChange=${(e) => setStart(e.target.value)} />
        <input type="date" value=${end} onChange=${(e) => setEnd(e.target.value)} />
        <button type="button" onClick=${load}>Refresh</button>
        <button type="button" onClick=${exportCsv}>Export CSV</button>
      </div>
      <p class="status">${status}</p>
      <table class="people-table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Total Time</th>
            <th>Days Present</th>
          </tr>
        </thead>
        <tbody>
          ${employees.map(
            (e) => html`
              <tr key=${e.name}>
                <td>${e.name}</td>
                <td>${formatDuration(e.totalMinutes)}</td>
                <td>${e.daysPresent}</td>
              </tr>
            `
          )}
        </tbody>
      </table>
    </section>
  `;
}

function AdminPage() {
  const [tab, setTab] = useState("corrections");

  return html`
    <div>
      <div class="camera-controls">
        <button type="button" disabled=${tab === "corrections"} onClick=${() => setTab("corrections")}>Daily Corrections</button>
        <button type="button" disabled=${tab === "report"} onClick=${() => setTab("report")}>Range Report</button>
      </div>
      ${tab === "corrections" ? html`<${DailyCorrections} />` : html`<${RangeReport} />`}
    </div>
  `;
}

ReactDOM.createRoot(document.getElementById("adminRoot")).render(html`<${AdminPage} />`);
