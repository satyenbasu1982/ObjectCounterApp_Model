const PEOPLE_DETAILS_URL = "/api/people/details";
const PEOPLE_URL = "/api/people";

const searchBox = document.getElementById("peopleSearch");
const status = document.getElementById("peopleStatus");
const tableBody = document.getElementById("peopleTableBody");
const sortButtons = document.querySelectorAll(".sort-btn");

let allPeople = [];
let sortColumn = "name";
let sortAscending = true;
let editingName = null;
let editError = null;

async function loadPeople() {
  status.textContent = "Loading...";
  try {
    const response = await fetch(PEOPLE_DETAILS_URL);
    if (!response.ok) {
      throw new Error(`Request failed: ${response.status}`);
    }
    allPeople = await response.json();
    status.textContent = "";
    render();
  } catch (err) {
    status.textContent = `Error: ${err.message}`;
  }
}

function getFilteredSorted() {
  const query = searchBox.value.trim().toLowerCase();
  let rows = allPeople.filter((p) => p.name.toLowerCase().includes(query));

  rows.sort((a, b) => {
    const va = a[sortColumn];
    const vb = b[sortColumn];
    if (va < vb) return sortAscending ? -1 : 1;
    if (va > vb) return sortAscending ? 1 : -1;
    return 0;
  });

  return rows;
}

function render() {
  updateSortIndicators();
  const rows = getFilteredSorted();
  tableBody.innerHTML = "";

  for (const person of rows) {
    const tr = document.createElement("tr");

    const thumbTd = document.createElement("td");
    if (person.thumbnail) {
      const img = document.createElement("img");
      img.src = person.thumbnail;
      img.alt = person.name;
      img.className = "thumbnail";
      thumbTd.appendChild(img);
    }
    tr.appendChild(thumbTd);

    const nameTd = document.createElement("td");
    const actionsTd = document.createElement("td");
    const photosTd = document.createElement("td");
    photosTd.textContent = person.photoCount;

    if (editingName === person.name) {
      const input = document.createElement("input");
      input.type = "text";
      input.value = person.name;
      input.className = "edit-name-input";
      nameTd.appendChild(input);

      if (editError) {
        const errorSpan = document.createElement("span");
        errorSpan.className = "edit-error";
        errorSpan.textContent = editError;
        nameTd.appendChild(errorSpan);
      }

      const saveBtn = document.createElement("button");
      saveBtn.type = "button";
      saveBtn.textContent = "Save";
      saveBtn.addEventListener("click", () => saveRename(person.name, input.value.trim()));

      const cancelBtn = document.createElement("button");
      cancelBtn.type = "button";
      cancelBtn.textContent = "Cancel";
      cancelBtn.addEventListener("click", () => {
        editingName = null;
        editError = null;
        render();
      });

      actionsTd.appendChild(saveBtn);
      actionsTd.appendChild(cancelBtn);
    } else {
      nameTd.textContent = person.name;

      const editBtn = document.createElement("button");
      editBtn.type = "button";
      editBtn.textContent = "Edit";
      editBtn.addEventListener("click", () => {
        editingName = person.name;
        editError = null;
        render();
      });

      const deleteBtn = document.createElement("button");
      deleteBtn.type = "button";
      deleteBtn.textContent = "Delete";
      deleteBtn.addEventListener("click", () => deletePerson(person.name));

      actionsTd.appendChild(editBtn);
      actionsTd.appendChild(deleteBtn);
    }

    tr.appendChild(nameTd);
    tr.appendChild(photosTd);
    tr.appendChild(actionsTd);
    tableBody.appendChild(tr);
  }
}

async function saveRename(oldName, newName) {
  if (!newName) {
    return;
  }

  try {
    const response = await fetch(`${PEOPLE_URL}/${encodeURIComponent(oldName)}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ newName })
    });

    if (response.status === 409) {
      editError = "That name is already in use.";
      render();
      return;
    }
    if (!response.ok) {
      throw new Error(`Request failed: ${response.status}`);
    }

    const person = allPeople.find((p) => p.name === oldName);
    if (person) {
      person.name = newName;
    }
    editingName = null;
    editError = null;
    render();
  } catch (err) {
    editError = err.message;
    render();
  }
}

async function deletePerson(name) {
  if (!confirm(`Delete ${name}? This cannot be undone.`)) {
    return;
  }

  await fetch(`${PEOPLE_URL}/${encodeURIComponent(name)}`, { method: "DELETE" });
  allPeople = allPeople.filter((p) => p.name !== name);
  render();
}

function updateSortIndicators() {
  for (const btn of sortButtons) {
    const column = btn.dataset.sort;
    const baseLabel = column === "name" ? "Name" : "Photos";
    if (column === sortColumn) {
      btn.textContent = `${baseLabel} ${sortAscending ? "▲" : "▼"}`;
    } else {
      btn.textContent = baseLabel;
    }
  }
}

searchBox.addEventListener("input", render);

for (const btn of sortButtons) {
  btn.addEventListener("click", () => {
    const column = btn.dataset.sort;
    if (sortColumn === column) {
      sortAscending = !sortAscending;
    } else {
      sortColumn = column;
      sortAscending = true;
    }
    render();
  });
}

loadPeople();
