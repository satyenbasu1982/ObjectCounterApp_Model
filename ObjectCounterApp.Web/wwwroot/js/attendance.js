const ATTENDANCE_URL = "/api/attendance";
const POLL_INTERVAL_MS = 5000;

const dateInput = document.getElementById("attendanceDate");
const refreshBtn = document.getElementById("refreshBtn");
const exportBtn = document.getElementById("exportBtn");
const status = document.getElementById("attendanceStatus");
const tableBody = document.getElementById("attendanceTableBody");

let pollTimer = null;
let currentEmployees = [];

function todayLocalDateString() {
  const d = new Date();
  const month = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${d.getFullYear()}-${month}-${day}`;
}

function formatTime(isoString) {
  const d = new Date(isoString);
  return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function formatDuration(totalMinutes) {
  const hours = Math.floor(totalMinutes / 60);
  const minutes = Math.round(totalMinutes % 60);
  return `${hours}h ${minutes}m`;
}

async function loadAttendance() {
  const date = dateInput.value || todayLocalDateString();
  status.textContent = "Loading...";

  try {
    const response = await fetch(`${ATTENDANCE_URL}/${date}`);
    if (!response.ok) {
      throw new Error(`Request failed: ${response.status}`);
    }

    const data = await response.json();
    currentEmployees = data.employees;
    render(data.employees);
    status.textContent = data.employees.length === 0 ? "No attendance recorded for this day." : "";
  } catch (err) {
    status.textContent = `Error: ${err.message}`;
  }
}

function addRow(cells, className) {
  const tr = document.createElement("tr");
  if (className) {
    tr.className = className;
  }
  for (const text of cells) {
    const td = document.createElement("td");
    td.textContent = text;
    tr.appendChild(td);
  }
  tableBody.appendChild(tr);
}

// Shows every entry/exit for the day, not just a rolled-up summary - a
// bolded summary row per employee (name, first in, last seen/out, total
// time, status), followed by one indented row per individual session so
// each separate visit that day is actually visible, not just folded into
// the total.
function render(employees) {
  tableBody.innerHTML = "";

  for (const employee of employees) {
    addRow(
      [
        employee.name,
        formatTime(employee.firstIn),
        formatTime(employee.lastSeenOrOut),
        formatDuration(employee.totalMinutes),
        employee.isPresent ? "Present" : "Left"
      ],
      "attendance-summary-row"
    );

    employee.sessions.forEach((session, index) => {
      const sessionMinutes = (new Date(session.end) - new Date(session.start)) / 60000;
      addRow(
        [
          `    ↳ Session ${index + 1}`,
          formatTime(session.start),
          formatTime(session.end),
          formatDuration(sessionMinutes),
          ""
        ],
        "attendance-session-row"
      );
    });
  }
}

function csvEscape(value) {
  const str = String(value);
  return /[",\r\n]/.test(str) ? `"${str.replace(/"/g, '""')}"` : str;
}

// Plain CSV, not a real .xlsx - Excel opens CSV natively with no extra
// dependency needed (client-side lib or a server-side package), matching
// this app's existing no-external-dependencies style. Mirrors the table
// exactly: a summary row per employee plus one row per individual session.
function exportToCsv() {
  const date = dateInput.value || todayLocalDateString();
  const rows = [["Name", "Type", "In", "Out", "Duration", "Status"]];

  for (const employee of currentEmployees) {
    rows.push([
      employee.name,
      "Summary",
      formatTime(employee.firstIn),
      formatTime(employee.lastSeenOrOut),
      formatDuration(employee.totalMinutes),
      employee.isPresent ? "Present" : "Left"
    ]);

    employee.sessions.forEach((session, index) => {
      const sessionMinutes = (new Date(session.end) - new Date(session.start)) / 60000;
      rows.push([
        employee.name,
        `Session ${index + 1}`,
        formatTime(session.start),
        formatTime(session.end),
        formatDuration(sessionMinutes),
        ""
      ]);
    });
  }

  const csv = rows.map((row) => row.map(csvEscape).join(",")).join("\r\n");
  const blob = new Blob(["﻿" + csv], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);

  const link = document.createElement("a");
  link.href = url;
  link.download = `attendance-${date}.csv`;
  link.click();
  URL.revokeObjectURL(url);
}

function restartPolling() {
  if (pollTimer !== null) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
  if (dateInput.value === todayLocalDateString()) {
    pollTimer = setInterval(loadAttendance, POLL_INTERVAL_MS);
  }
}

dateInput.value = todayLocalDateString();
dateInput.addEventListener("change", () => {
  loadAttendance();
  restartPolling();
});
refreshBtn.addEventListener("click", loadAttendance);
exportBtn.addEventListener("click", exportToCsv);

loadAttendance();
restartPolling();
