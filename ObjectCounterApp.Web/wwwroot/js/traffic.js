// Build-less React, same approach as admin.js: real React + ReactDOM loaded
// straight from a CDN, htm for JSX-like template syntax, no npm/bundler -
// this page ships as a plain static file exactly like every other page in
// wwwroot.
import React from "https://esm.sh/react@18";
import ReactDOM from "https://esm.sh/react-dom@18/client";
import htm from "https://esm.sh/htm@3";

const html = htm.bind(React.createElement);
const { useState, useEffect, useCallback } = React;

function todayLocalDateString() {
  const d = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

// Same shape as admin.js's error handling: our own controller's
// BadRequest("...")/NotFound() responses are plain text (or an
// auto-generated ProblemDetails with a readable `title`), already fine to
// show as-is; only a raw model-binding validation response (`errors`) needs
// translating to something readable.
async function extractErrorMessage(res) {
  const text = await res.text();
  if (!text) {
    return `Request failed (${res.status}).`;
  }

  try {
    const problem = JSON.parse(text);
    if (problem && typeof problem === "object" && problem.errors) {
      return "One or more fields have an invalid value. Please check your input and try again.";
    }
    if (problem && typeof problem === "object" && problem.title) {
      return problem.title;
    }
  } catch {
    // Not JSON - one of our own plain-text messages.
  }

  return text;
}

function TrafficPage() {
  const [cameraId, setCameraId] = useState("default");
  const [date, setDate] = useState(todayLocalDateString());
  const [occupancy, setOccupancy] = useState(null);
  const [summary, setSummary] = useState(null);
  const [status, setStatus] = useState("");

  const loadOccupancy = useCallback(async () => {
    try {
      const res = await fetch(`/api/traffic/${encodeURIComponent(cameraId)}/live`);
      if (!res.ok) {
        throw new Error(await extractErrorMessage(res));
      }
      const data = await res.json();
      setOccupancy(data.occupancy);
    } catch (err) {
      setStatus(`Error: ${err.message}`);
    }
  }, [cameraId]);

  const loadDay = useCallback(async () => {
    setStatus("Loading...");
    try {
      const res = await fetch(`/api/traffic/${encodeURIComponent(cameraId)}/${date}`);
      if (!res.ok) {
        throw new Error(await extractErrorMessage(res));
      }
      setSummary(await res.json());
      setStatus("");
    } catch (err) {
      setStatus(`Error: ${err.message}`);
    }
  }, [cameraId, date]);

  useEffect(() => {
    loadDay();
  }, [loadDay]);

  // Live occupancy is polled independently of the day summary, on its own
  // short interval, while this page stays open - cleared on unmount so
  // navigating away doesn't leave a timer running against a torn-down page.
  useEffect(() => {
    loadOccupancy();
    const timer = setInterval(loadOccupancy, 3000);
    return () => clearInterval(timer);
  }, [loadOccupancy]);

  return html`
    <section class="panel">
      <div class="camera-controls">
        <input type="text" value=${cameraId} onChange=${(e) => setCameraId(e.target.value)} placeholder="Camera ID" />
        <input type="date" value=${date} onChange=${(e) => setDate(e.target.value)} />
        <button type="button" onClick=${loadDay}>Refresh</button>
      </div>
      <p class="status">${status}</p>
      <p><strong>Currently in view:</strong> ${occupancy === null ? "..." : occupancy}</p>
      <p><strong>Total visits (${date}):</strong> ${summary ? summary.totalVisits : "..."}</p>
      <table class="people-table">
        <thead>
          <tr>
            <th>Hour</th>
            <th>Visits</th>
          </tr>
        </thead>
        <tbody>
          ${summary
            ? summary.hourlyCounts.map(
                (count, hour) => html`
                  <tr key=${hour}>
                    <td>${String(hour).padStart(2, "0")}:00</td>
                    <td>${count}</td>
                  </tr>
                `
              )
            : null}
        </tbody>
      </table>
    </section>
  `;
}

ReactDOM.createRoot(document.getElementById("trafficRoot")).render(html`<${TrafficPage} />`);
