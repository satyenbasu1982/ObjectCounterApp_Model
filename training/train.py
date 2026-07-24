"""Fine-tunes YOLOv8n on the person / person-like dataset and exports to ONNX.

Run from an activated venv with `training/requirements.txt` installed:
    python training/train.py
"""

from ultralytics import YOLO

DATA_YAML = r"D:\Project\ObjectCounterApp_Model\human - human like.v1i.yolov8\data.yaml"
RUN_NAME = "person_detector"


def main():
    model = YOLO("yolov8n.pt")  # auto-downloads pretrained COCO weights on first run
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

    best_weights = f"runs/{RUN_NAME}/weights/best.pt"
    best_model = YOLO(best_weights)
    best_model.export(format="onnx", imgsz=640, opset=12, simplify=True)

    print(f"\nDone. ONNX export: runs/{RUN_NAME}/weights/best.onnx")
    print("Copy/rename it to: ObjectCounterApp/human-detector.onnx")


if __name__ == "__main__":
    main()
