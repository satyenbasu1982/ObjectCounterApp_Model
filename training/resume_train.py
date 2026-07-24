"""Resumes the interrupted person_detector training run from last.pt.

Run from an activated venv with `training/requirements.txt` installed:
    python training/resume_train.py
"""

from ultralytics import YOLO

LAST_WEIGHTS = "runs/detect/runs/person_detector/weights/last.pt"


def main():
    model = YOLO(LAST_WEIGHTS)
    model.train(resume=True)

    best_weights = "runs/detect/runs/person_detector/weights/best.pt"
    best_model = YOLO(best_weights)
    best_model.export(format="onnx", imgsz=640, opset=12, simplify=True)

    print("\nDone. ONNX export: runs/detect/runs/person_detector/weights/best.onnx")
    print("Copy/rename it to: ObjectCounterApp/human-detector.onnx")


if __name__ == "__main__":
    main()
