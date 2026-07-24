const DETECT_URL = "/api/detect";
const MAX_CONSECUTIVE_ERRORS = 5;
const ERROR_RETRY_DELAY_MS = 500;

// Tracking tuning: raw per-frame detections are independent (no memory of
// previous frames), so borderline identity matches flip name<->generic and
// a single missed frame makes a box vanish. These smooth that out.
const IOU_MATCH_THRESHOLD = 0.3;   // min overlap to treat a new box as "the same person" as an existing track
const BOX_SMOOTHING_ALPHA = 0.4;   // lower = smoother/laggier box movement, higher = snappier/jitterier
const IDENTITY_CONFIRM_FRAMES = 3; // consecutive frames a new label must win before the displayed label switches
const MAX_MISSED_FRAMES = 3;       // frames a track survives with no matching detection before it's dropped

const startBtn = document.getElementById("startCameraBtn");
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
let tracks = [];
let nextTrackId = 0;

function resizeCanvasToElement(canvas, referenceEl) {
  canvas.width = referenceEl.clientWidth;
  canvas.height = referenceEl.clientHeight;
}

// Plain IoU under-matches when comparing a tight face box against a much
// bigger person box (e.g. a frame where no face box came back that round) -
// falling back to "how much of the smaller box sits inside the bigger one"
// keeps the same person's track matched across box-size changes.
function overlapScore(a, b) {
  const x1 = Math.max(a.x1, b.x1);
  const y1 = Math.max(a.y1, b.y1);
  const x2 = Math.min(a.x2, b.x2);
  const y2 = Math.min(a.y2, b.y2);
  const interArea = Math.max(0, x2 - x1) * Math.max(0, y2 - y1);
  const areaA = (a.x2 - a.x1) * (a.y2 - a.y1);
  const areaB = (b.x2 - b.x1) * (b.y2 - b.y1);
  const union = areaA + areaB - interArea;
  const iouScore = union <= 0 ? 0 : interArea / union;
  const smaller = Math.min(areaA, areaB);
  const containment = smaller <= 0 ? 0 : interArea / smaller;
  return Math.max(iouScore, containment);
}

function smoothBox(oldBox, newBox) {
  const a = BOX_SMOOTHING_ALPHA;
  return {
    x1: oldBox.x1 + (newBox.x1 - oldBox.x1) * a,
    y1: oldBox.y1 + (newBox.y1 - oldBox.y1) * a,
    x2: oldBox.x2 + (newBox.x2 - oldBox.x2) * a,
    y2: oldBox.y2 + (newBox.y2 - oldBox.y2) * a
  };
}

// Matches this frame's raw detections onto existing tracks (by box overlap),
// smooths box movement, debounces identity-label changes, and keeps a
// briefly-missed track alive at its last known box instead of dropping it.
function updateTracks(detections) {
  const matchedTrackIds = new Set();

  for (const det of detections) {
    const rawLabel = !det.isLikelyReal ? "Possibly not real" : (det.identityName || det.label);
    const hasFaceBox = det.faceX1 != null;
    // Prefer the face box (tight to the actual face) over the person/body box
    // whenever identify found one - the whole point is the box tracking the
    // face regardless of how the person is sitting/posed.
    const box = hasFaceBox
      ? { x1: det.faceX1, y1: det.faceY1, x2: det.faceX2, y2: det.faceY2 }
      : { x1: det.x1, y1: det.y1, x2: det.x2, y2: det.y2 };

    let bestTrack = null;
    let bestScore = IOU_MATCH_THRESHOLD;
    for (const track of tracks) {
      if (matchedTrackIds.has(track.id)) continue;
      const score = overlapScore(track.box, box);
      if (score > bestScore) {
        bestScore = score;
        bestTrack = track;
      }
    }

    if (bestTrack) {
      matchedTrackIds.add(bestTrack.id);
      // Only move the drawn box when this frame actually found a face box -
      // a frame where the face was momentarily missed (occlusion/glare) still
      // matches and keeps the person "seen", but shouldn't snap the box out
      // to the much bigger person box for one frame.
      if (hasFaceBox) {
        bestTrack.box = smoothBox(bestTrack.box, box);
      }
      bestTrack.score = det.score;
      bestTrack.isLikelyReal = det.isLikelyReal;
      bestTrack.missedFrames = 0;

      if (rawLabel === bestTrack.label) {
        bestTrack.pendingLabel = null;
        bestTrack.pendingCount = 0;
      } else if (rawLabel === bestTrack.pendingLabel) {
        bestTrack.pendingCount++;
        if (bestTrack.pendingCount >= IDENTITY_CONFIRM_FRAMES) {
          bestTrack.label = rawLabel;
          bestTrack.pendingLabel = null;
          bestTrack.pendingCount = 0;
        }
      } else {
        bestTrack.pendingLabel = rawLabel;
        bestTrack.pendingCount = 1;
      }
    } else {
      matchedTrackIds.add(nextTrackId);
      tracks.push({
        id: nextTrackId++,
        box,
        label: rawLabel,
        score: det.score,
        isLikelyReal: det.isLikelyReal,
        missedFrames: 0,
        pendingLabel: null,
        pendingCount: 0
      });
    }
  }

  tracks = tracks.filter((track) => {
    if (!matchedTrackIds.has(track.id)) {
      track.missedFrames++;
    }
    return track.missedFrames <= MAX_MISSED_FRAMES;
  });
}

function drawTracks(canvas) {
  const ctx = canvas.getContext("2d");
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.lineWidth = 2;
  ctx.font = "14px system-ui, sans-serif";
  ctx.textBaseline = "top";

  for (const track of tracks) {
    const x = track.box.x1 * canvas.width;
    const y = track.box.y1 * canvas.height;
    const w = (track.box.x2 - track.box.x1) * canvas.width;
    const h = (track.box.y2 - track.box.y1) * canvas.height;

    const color = track.isLikelyReal ? "#00e676" : "#ffab00";
    ctx.globalAlpha = track.missedFrames > 0 ? 0.5 : 1;
    ctx.strokeStyle = color;
    ctx.strokeRect(x, y, w, h);

    const label = `${track.label} ${Math.round(track.score * 100)}%`;
    const labelWidth = ctx.measureText(label).width + 8;
    const labelHeight = 18;
    const labelY = y - labelHeight >= 0 ? y - labelHeight : y;

    ctx.fillStyle = color;
    ctx.fillRect(x, labelY, labelWidth, labelHeight);
    ctx.fillStyle = "#000000";
    ctx.fillText(label, x + 4, labelY + 2);
    ctx.globalAlpha = 1;
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

async function detectLoop() {
  while (running) {
    try {
      captureCanvas.width = video.videoWidth;
      captureCanvas.height = video.videoHeight;
      captureCtx.drawImage(video, 0, 0, captureCanvas.width, captureCanvas.height);

      const blob = await new Promise((resolve) => captureCanvas.toBlob(resolve, "image/jpeg", 0.8));
      if (!running) break;

      const result = await detect(blob, true);
      if (!running) break;

      resizeCanvasToElement(canvas, video);
      updateTracks(result.detections);
      drawTracks(canvas);
      status.textContent = `Found ${tracks.length} person(s).`;
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
    return;
  }

  video.srcObject = stream;
  stage.hidden = false;
  stopBtn.disabled = false;
  consecutiveErrors = 0;
  tracks = [];
  nextTrackId = 0;
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
  tracks = [];
  canvas.getContext("2d").clearRect(0, 0, canvas.width, canvas.height);
  status.textContent = "Camera stopped.";
  startBtn.disabled = false;
  stopBtn.disabled = true;
}

startBtn.addEventListener("click", startCamera);
stopBtn.addEventListener("click", stopCamera);
window.addEventListener("pagehide", stopCamera);

video.addEventListener("loadedmetadata", () => {
  resizeCanvasToElement(canvas, video);
});

new ResizeObserver(() => resizeCanvasToElement(canvas, video)).observe(video);
