"""Fine-tunes the existing person_detector at higher resolution (800px) to
help it separate closely-overlapping people (e.g. an adult holding a swaddled
infant), which the 640px run consistently merged into a single box.

Warm-starts from the already-trained best.pt (640px run) rather than the raw
COCO weights, since it already knows the person / person-like distinction --
this should converge faster than training from scratch.

Started at 960px first, but epoch-1 validation alone took ~29 minutes (vs
~20s at 640px) due to the much larger proposal count blowing up NMS cost
during validation -- projected to ~36h for the full 60-epoch run. Dropped to
800px as a middle ground: meaningfully higher resolution than 640px, without
that blowup.

Run from an activated venv with `training/requirements.txt` installed:
    python training/train_hires.py
"""

from ultralytics import YOLO

DATA_YAML = r"D:\Project\ObjectCounterApp_Model\human - human like.v1i.yolov8\data.yaml"
WARM_START_WEIGHTS = "runs/detect/runs/person_detector/weights/best.pt"
RUN_NAME = "person_detector_800"


def main():
    model = YOLO(WARM_START_WEIGHTS)
    model.train(
        data=DATA_YAML,
        epochs=60,
        imgsz=800,
        batch=8,
        device="cpu",
        patience=15,
        project="runs",
        name=RUN_NAME,
    )

    save_dir = model.trainer.save_dir
    best_weights = save_dir / "weights" / "best.pt"
    best_model = YOLO(str(best_weights))
    best_model.export(format="onnx", imgsz=800, opset=12, simplify=True)

    print(f"\nDone. ONNX export: {save_dir / 'weights' / 'best.onnx'}")
    print("Copy/rename it to: ObjectCounterApp/human-detector.onnx")


if __name__ == "__main__":
    main()
