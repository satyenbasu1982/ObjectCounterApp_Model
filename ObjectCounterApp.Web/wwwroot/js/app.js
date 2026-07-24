const DETECT_URL = "/api/detect";
const VIDEO_DETECT_INTERVAL_MS = 400;

function setupDropzone(dropzoneEl, inputEl, onFile) {
  dropzoneEl.addEventListener("click", () => inputEl.click());

  inputEl.addEventListener("change", () => {
    if (inputEl.files.length > 0) {
      onFile(inputEl.files[0]);
    }
  });

  dropzoneEl.addEventListener("dragover", (e) => {
    e.preventDefault();
    dropzoneEl.classList.add("dragover");
  });

  dropzoneEl.addEventListener("dragleave", () => {
    dropzoneEl.classList.remove("dragover");
  });

  dropzoneEl.addEventListener("drop", (e) => {
    e.preventDefault();
    dropzoneEl.classList.remove("dragover");
    if (e.dataTransfer.files.length > 0) {
      onFile(e.dataTransfer.files[0]);
    }
  });
}

function resizeCanvasToElement(canvas, referenceEl) {
  canvas.width = referenceEl.clientWidth;
  canvas.height = referenceEl.clientHeight;
}

function drawDetections(canvas, detections) {
  const ctx = canvas.getContext("2d");
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.lineWidth = 2;
  ctx.font = "14px system-ui, sans-serif";
  ctx.textBaseline = "top";

  for (const d of detections) {
    const x = d.x1 * canvas.width;
    const y = d.y1 * canvas.height;
    const w = (d.x2 - d.x1) * canvas.width;
    const h = (d.y2 - d.y1) * canvas.height;

    const color = d.isLikelyReal ? "#00e676" : "#ffab00";
    ctx.strokeStyle = color;
    ctx.strokeRect(x, y, w, h);

    const labelText = !d.isLikelyReal ? "Possibly not real" : (d.identityName || d.label);
    const label = `${labelText} ${Math.round(d.score * 100)}%`;
    const labelWidth = ctx.measureText(label).width + 8;
    const labelHeight = 18;
    const labelY = y - labelHeight >= 0 ? y - labelHeight : y;

    ctx.fillStyle = color;
    ctx.fillRect(x, labelY, labelWidth, labelHeight);
    ctx.fillStyle = "#000000";
    ctx.fillText(label, x + 4, labelY + 2);
  }
}

async function detect(blob, identify) {
  const formData = new FormData();
  formData.append("file", blob, "frame.jpg");

  const url = identify ? `${DETECT_URL}?identify=true` : DETECT_URL;
  const response = await fetch(url, {
    method: "POST",
    body: formData
  });

  if (!response.ok) {
    throw new Error(`Detect request failed: ${response.status}`);
  }

  return response.json();
}

// ---------- Image flow ----------

(function setupImageFlow() {
  const dropzone = document.getElementById("imageDropzone");
  const input = document.getElementById("imageInput");
  const status = document.getElementById("imageStatus");
  const stage = document.getElementById("imageStage");
  const img = document.getElementById("imagePreview");
  const canvas = document.getElementById("imageCanvas");

  setupDropzone(dropzone, input, async (file) => {
    status.textContent = "Detecting...";
    stage.hidden = false;
    img.src = URL.createObjectURL(file);

    img.onload = async () => {
      resizeCanvasToElement(canvas, img);

      try {
        const result = await detect(file, true);
        resizeCanvasToElement(canvas, img);
        drawDetections(canvas, result.detections);
        status.textContent = `Found ${result.count} person(s).`;
      } catch (err) {
        status.textContent = `Error: ${err.message}`;
      }
    };
  });

  new ResizeObserver(() => resizeCanvasToElement(canvas, img)).observe(img);
})();

// ---------- Video flow ----------

(function setupVideoFlow() {
  const dropzone = document.getElementById("videoDropzone");
  const input = document.getElementById("videoInput");
  const status = document.getElementById("videoStatus");
  const stage = document.getElementById("videoStage");
  const video = document.getElementById("videoPreview");
  const canvas = document.getElementById("videoCanvas");

  const captureCanvas = document.createElement("canvas");
  const captureCtx = captureCanvas.getContext("2d");

  let intervalId = null;
  let inFlight = false;

  async function captureAndDetect() {
    if (inFlight || video.paused || video.ended) {
      return;
    }

    captureCanvas.width = video.videoWidth;
    captureCanvas.height = video.videoHeight;
    captureCtx.drawImage(video, 0, 0, captureCanvas.width, captureCanvas.height);

    inFlight = true;
    captureCanvas.toBlob(async (blob) => {
      try {
        const result = await detect(blob);
        resizeCanvasToElement(canvas, video);
        drawDetections(canvas, result.detections);
        status.textContent = `Found ${result.count} person(s).`;
      } catch (err) {
        status.textContent = `Error: ${err.message}`;
      } finally {
        inFlight = false;
      }
    }, "image/jpeg", 0.8);
  }

  setupDropzone(dropzone, input, (file) => {
    status.textContent = "Loaded. Press play to start detecting.";
    stage.hidden = false;
    video.src = URL.createObjectURL(file);
  });

  video.addEventListener("loadedmetadata", () => {
    resizeCanvasToElement(canvas, video);
  });

  video.addEventListener("play", () => {
    if (intervalId === null) {
      intervalId = setInterval(captureAndDetect, VIDEO_DETECT_INTERVAL_MS);
    }
  });

  function stopDetectionLoop() {
    if (intervalId !== null) {
      clearInterval(intervalId);
      intervalId = null;
    }
  }

  video.addEventListener("pause", stopDetectionLoop);
  video.addEventListener("ended", stopDetectionLoop);

  new ResizeObserver(() => resizeCanvasToElement(canvas, video)).observe(video);
})();
