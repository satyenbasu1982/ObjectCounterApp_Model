const DETECT_URL = "/api/detect";
const MAX_CONSECUTIVE_ERRORS = 5;
const ERROR_RETRY_DELAY_MS = 500;

// Tracking tuning: raw per-frame detections are independent (no memory of
// previous frames), so borderline identity matches flip name<->generic and
// a single missed frame makes a box vanish. These smooth that out.
const IOU_MATCH_THRESHOLD = 0.3;  // min overlap to treat a new box as "the same person" as an existing track
const BOX_SMOOTHING_ALPHA = 0.4;  // lower = smoother/laggier box movement, higher = snappier/jitterier
const LABEL_HISTORY_SIZE = 5;     // recent raw labels kept per track; displayed label = the majority within this window
const MAX_MISSED_FRAMES = 3;      // frames a track survives with no matching detection before it's dropped

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
let tracks = [];
let nextTrackId = 0;

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

// Picks whichever label occurs most often in a track's recent label history,
// breaking ties in favor of the currently-displayed label so an even split
// doesn't cause pointless flicker. A single noisy frame only dilutes the
// window slightly rather than resetting progress toward the correct answer,
// so this converges faster than requiring N strictly consecutive matches.
function pickMajorityLabel(history, currentLabel) {
  const counts = new Map();
  for (const label of history) {
    counts.set(label, (counts.get(label) || 0) + 1);
  }

  let bestLabel = currentLabel;
  let bestCount = counts.get(currentLabel) || 0;
  for (const [label, count] of counts) {
    if (count > bestCount) {
      bestCount = count;
      bestLabel = label;
    }
  }
  return bestLabel;
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

      // Only a frame where a face was actually found carries real identity
      // information (a genuine match, or a genuine "Unknown"). A frame where
      // detection simply failed - very common under glare/backlight - isn't
      // evidence the person changed, it's just a gap, so it must not be
      // allowed to erode an already-established name back to the generic
      // fallback. Once a face has been seen, the label only updates from
      // further face-based results.
      if (hasFaceBox) {
        bestTrack.labelHistory.push(rawLabel);
        if (bestTrack.labelHistory.length > LABEL_HISTORY_SIZE) {
          bestTrack.labelHistory.shift();
        }
        bestTrack.label = pickMajorityLabel(bestTrack.labelHistory, bestTrack.label);
      }
    } else if (hasFaceBox) {
      // Only spawn a new track once a face has actually been found and
      // identity-checked (a real name or "Unknown") - this page only cares
      // about resolved identities, so a bare person/body detection (no face
      // found yet) never puts a generic "Person" box on screen. An unmatched
      // bare detection is simply ignored here.
      matchedTrackIds.add(nextTrackId);
      tracks.push({
        id: nextTrackId++,
        box,
        label: rawLabel,
        score: det.score,
        isLikelyReal: det.isLikelyReal,
        missedFrames: 0,
        labelHistory: [rawLabel]
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
