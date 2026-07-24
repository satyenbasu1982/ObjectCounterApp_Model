"""Fine-tunes the person_detector on the combined dataset (original
person/person-like set + the Baby-Detection infant dataset merged in as
extra "person" examples), to teach it what an infant's body/face looks like
-- the original dataset had essentially no babies, which is why the model
merged a held infant into the same box as the adult holding it.

Warm-starts from the existing 640px best.pt and trains at 640px (960/800
experiments showed resolution wasn't the limiting factor -- data was).

Run from an activated venv with `training/requirements.txt` installed:
    python training/train_combined.py
"""

from ultralytics import YOLO

DATA_YAML = r"D:\Project\ObjectCounterApp_Model\combined-person-dataset\data.yaml"
WARM_START_WEIGHTS = "runs/detect/runs/person_detector/weights/best.pt"
RUN_NAME = "person_detector_combined"


def main():
    model = YOLO(WARM_START_WEIGHTS)
    model.train(
        data=DATA_YAML,
        epochs=60,
        imgsz=640,
        batch=8,
        device="cpu",
        patience=15,
        project="runs",
        name=RUN_NAME,
    )

    save_dir = model.trainer.save_dir
    best_weights = save_dir / "weights" / "best.pt"
    best_model = YOLO(str(best_weights))
    best_model.export(format="onnx", imgsz=640, opset=12, simplify=True)

    print(f"\nDone. ONNX export: {save_dir / 'weights' / 'best.onnx'}")
    print("Copy/rename it to: ObjectCounterApp/human-detector.onnx")


if __name__ == "__main__":
    main()
