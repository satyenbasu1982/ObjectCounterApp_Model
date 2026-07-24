# Training the human detector

Fine-tunes YOLOv8n on the `person` / `person-like` dataset
(`../../human - human like.v1i.yolov8/`) and exports it to ONNX for the C# app.

No NVIDIA GPU is required/used here — this trains on CPU.

## Setup

```
python -m venv training\.venv
training\.venv\Scripts\Activate.ps1
pip install --upgrade pip
pip install -r training\requirements.txt
```

If `pip install` can't find a compatible `torch` build for your Python version
(this happens with very new Python releases), install Python 3.11 or 3.12
instead (`winget install Python.Python.3.12` or `py -3.12` if already present)
and recreate the venv with that interpreter.

## Train + export

```
python training\train.py
```

This trains for up to 60 epochs (early-stops after 15 epochs with no
improvement) at 640x640, batch size 8. On CPU with ~650 training images this
can take a couple of hours. If it's too slow, edit `train.py` and lower
`imgsz` to 416 and/or `epochs` to 30.

On success it prints the path to the exported ONNX file:
`runs/person_detector/weights/best.onnx`

## Wire it into the app

Copy/rename the exported file to the app's project root:

```
copy runs\person_detector\weights\best.onnx ..\human-detector.onnx
```

Then rebuild the app (`dotnet build` from `ObjectCounterApp/`) — the csproj
copies `human-detector.onnx` into the build output automatically.
