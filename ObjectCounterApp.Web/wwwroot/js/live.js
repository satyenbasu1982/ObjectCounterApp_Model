const DETECT_URL = "/api/detect";
const MAX_CONSECUTIVE_ERRORS = 5;
const ERROR_RETRY_DELAY_MS = 500;

const startBtn = document.getElementById("startCameraBtn");
const startAttendanceBtn = document.getElementById("startAttendanceBtn");
const stopBtn = document.getElementById("stopCameraBtn");
const status = document.getElementById("liveStatus");
const stage = document.getElementById("liveStage");
const video = document.getElementById("livePreview");
const canvas = document.getElementById("liveCanvas");

const captureCanvas = document.createElement("canvas");
const captureCtx = captureCanvas.getContext("2d");

let stream = null;
let running = false;
let consecutiveErrors = 0;

// True whenever this session should log attendance - either a kiosk device
// opened as live.html?autostart=true (unattended, no button click), or
// someone explicitly clicked "Start Camera (Attendance)" below. Plain
// "Start Camera" never records attendance, so testing recognition at your
// desk doesn't spuriously log you as "in the office".
let isKioskMode = new URLSearchParams(window.location.search).get("autostart") === "true";

function resizeCanvasToElement(canvas, referenceEl) {
  canvas.width = referenceEl.clientWidth;
  canvas.height = referenceEl.clientHeight;
}

// Tracking (box smoothing, IoU matching across frames, identity-lock voting,
// brief-occlusion survival) is now done server-side per track - see
// MultiObjectTracker.cs. This just draws whatever the server says is
// currently live; there's no client-side tracking memory left to keep in
// sync, so every render is a plain function of the latest response.
function renderDetections(canvas, detections) {
  const ctx = canvas.getContext("2d");
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.lineWidth = 2;
  ctx.font = "14px system-ui, sans-serif";
  ctx.textBaseline = "top";

  for (const det of detections) {
    // Prefer the face box (tight to the actual face) over the person/body
    // box whenever identify found one - purely a rendering choice, not
    // tracking logic.
    const hasFaceBox = det.faceX1 != null;
    const box = hasFaceBox
      ? { x1: det.faceX1, y1: det.faceY1, x2: det.faceX2, y2: det.faceY2 }
      : { x1: det.x1, y1: det.y1, x2: det.x2, y2: det.y2 };

    // Before a track's identity has locked, fall back to this frame's raw
    // result so a brand-new track shows something immediately instead of a
    // blank label.
    const label = !det.isLikelyReal
      ? "Possibly not real"
      : (det.isIdentityLocked ? det.lockedIdentityName : (det.identityName || det.label));

    const x = box.x1 * canvas.width;
    const y = box.y1 * canvas.height;
    const w = (box.x2 - box.x1) * canvas.width;
    const h = (box.y2 - box.y1) * canvas.height;

    const color = det.isLikelyReal ? "#00e676" : "#ffab00";
    ctx.globalAlpha = det.isCoasting ? 0.5 : 1;
    ctx.strokeStyle = color;
    ctx.strokeRect(x, y, w, h);

    const labelText = `${label} ${Math.round(det.score * 100)}%`;
    const labelWidth = ctx.measureText(labelText).width + 8;
    const labelHeight = 18;
    const labelY = y - labelHeight >= 0 ? y - labelHeight : y;

    ctx.fillStyle = color;
    ctx.fillRect(x, labelY, labelWidth, labelHeight);
    ctx.fillStyle = "#000000";
    ctx.fillText(labelText, x + 4, labelY + 2);
    ctx.globalAlpha = 1;
  }
}

async function detect(blob, identify) {
  const formData = new FormData();
  formData.append("file", blob, "frame.jpg");

  let url = DETECT_URL;
  if (identify) {
    url += "?identify=true";
    if (isKioskMode) {
      url += "&attendance=true";
    }
  }
  const response = await fetch(url, {
    method: "POST",
    body: formData
  });

  if (!response.ok) {
    throw new Error(`Detect request failed: ${response.status}`);
  }

  return response.json();
}

async function detectLoop() {
  while (running) {
    try {
      if (video.videoWidth === 0 || video.videoHeight === 0) {
        // Right after startCamera() attaches the stream, videoWidth/Height
        // can still be 0 for a moment until the browser loads the stream's
        // metadata - capturing now would produce a zero-size frame, and
        // toBlob resolves that as null rather than a Blob, breaking the
        // upload. Wait a beat instead of treating this as a real error.
        await new Promise((resolve) => setTimeout(resolve, 100));
        continue;
      }

      captureCanvas.width = video.videoWidth;
      captureCanvas.height = video.videoHeight;
      captureCtx.drawImage(video, 0, 0, captureCanvas.width, captureCanvas.height);

      const blob = await new Promise((resolve) => captureCanvas.toBlob(resolve, "image/jpeg", 0.8));
      if (!running) break;

      const result = await detect(blob, true);
      if (!running) break;

      resizeCanvasToElement(canvas, video);
      renderDetections(canvas, result.detections);
      status.textContent = `Found ${result.detections.length} person(s).`;
      consecutiveErrors = 0;
    } catch (err) {
      consecutiveErrors++;
      status.textContent = `Error: ${err.message}`;
      if (consecutiveErrors >= MAX_CONSECUTIVE_ERRORS) {
        stopCamera();
        break;
      }
      await new Promise((resolve) => setTimeout(resolve, ERROR_RETRY_DELAY_MS));
    }
  }
}

async function startCamera() {
  startBtn.disabled = true;
  startAttendanceBtn.disabled = true;
  status.textContent = "Requesting camera access...";

  try {
    stream = await navigator.mediaDevices.getUserMedia({
      video: {
        facingMode: "user",
        width: { ideal: 1280 },
        height: { ideal: 720 }
      },
      audio: false
    });
  } catch (err) {
    status.textContent = `Error: ${err.message}`;
    startBtn.disabled = false;
    startAttendanceBtn.disabled = false;
    return;
  }

  video.srcObject = stream;
  stage.hidden = false;
  stopBtn.disabled = false;
  consecutiveErrors = 0;
  running = true;
  status.textContent = "Camera on. Detecting...";
  detectLoop();
}

function stopCamera() {
  running = false;

  if (stream) {
    stream.getTracks().forEach((track) => track.stop());
    stream = null;
  }

  video.srcObject = null;
  stage.hidden = true;
  canvas.getContext("2d").clearRect(0, 0, canvas.width, canvas.height);
  status.textContent = "Camera stopped.";
  startBtn.disabled = false;
  startAttendanceBtn.disabled = false;
  stopBtn.disabled = true;
}

startBtn.addEventListener("click", () => {
  isKioskMode = false;
  startCamera();
});
startAttendanceBtn.addEventListener("click", () => {
  isKioskMode = true;
  startCamera();
});
stopBtn.addEventListener("click", stopCamera);
window.addEventListener("pagehide", stopCamera);

video.addEventListener("loadedmetadata", () => {
  resizeCanvasToElement(canvas, video);
});

new ResizeObserver(() => resizeCanvasToElement(canvas, video)).observe(video);

if (isKioskMode) {
  startCamera();
}
