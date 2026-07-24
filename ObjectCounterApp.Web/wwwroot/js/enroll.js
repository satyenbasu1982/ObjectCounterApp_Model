const PEOPLE_URL = "/api/people";

const form = document.getElementById("enrollForm");
const nameInput = document.getElementById("enrollName");
const photosInput = document.getElementById("enrollPhotos");
const status = document.getElementById("enrollStatus");
const list = document.getElementById("peopleList");

async function refreshPeopleList() {
  const response = await fetch(PEOPLE_URL);
  const result = await response.json();
  list.innerHTML = "";
  for (const name of result.names) {
    const li = document.createElement("li");
    li.textContent = name;
    const removeBtn = document.createElement("button");
    removeBtn.type = "button";
    removeBtn.textContent = "×";
    removeBtn.title = `Remove ${name}`;
    removeBtn.addEventListener("click", async () => {
      await fetch(`${PEOPLE_URL}/${encodeURIComponent(name)}`, { method: "DELETE" });
      refreshPeopleList();
    });
    li.appendChild(removeBtn);
    list.appendChild(li);
  }
}

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  const name = nameInput.value.trim();
  const files = photosInput.files;
  if (!name || files.length === 0) {
    return;
  }

  status.textContent = "Saving...";
  const formData = new FormData();
  formData.append("name", name);
  for (const file of files) {
    formData.append("photos", file);
  }

  try {
    const response = await fetch(PEOPLE_URL, { method: "POST", body: formData });
    if (!response.ok) {
      throw new Error(`Save request failed: ${response.status}`);
    }
    const result = await response.json();
    status.textContent = `Saved ${result.enrolledPhotos} photo(s) for ${result.name}` +
      (result.failedPhotos > 0 ? ` (${result.failedPhotos} photo(s) had no detectable face)` : "");
    form.reset();
    refreshPeopleList();
  } catch (err) {
    status.textContent = `Error: ${err.message}`;
  }
});

refreshPeopleList();
