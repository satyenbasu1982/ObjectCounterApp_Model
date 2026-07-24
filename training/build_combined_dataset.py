"""Merges the Baby-Detection (infant sleep-position) dataset into the
person / person-like dataset as additional "person" examples.

The baby dataset has 3 classes (prone, sideways, suspine) that are all just
"a baby" -- for our purposes they all remap to class 0 ("person"). Output is
written to a new combined-person-dataset/ directory; neither source dataset
is modified.

Run from an activated venv:
    python training/build_combined_dataset.py
"""

import shutil
from pathlib import Path

HUMAN_DIR = Path(r"D:\Project\ObjectCounterApp_Model\human - human like.v1i.yolov8")
BABY_DIR = Path(r"D:\Project\ObjectCounterApp_Model\Baby-Detection.v1i.yolov8")
OUT_DIR = Path(r"D:\Project\ObjectCounterApp_Model\combined-person-dataset")

SPLITS = ["train", "valid", "test"]
PERSON_CLASS = 0  # index in the human/human-like dataset's names: ['person', 'person-like']


def copy_human_split(split: str) -> int:
    src_img = HUMAN_DIR / split / "images"
    src_lbl = HUMAN_DIR / split / "labels"
    dst_img = OUT_DIR / split / "images"
    dst_lbl = OUT_DIR / split / "labels"
    count = 0
    for img_path in src_img.iterdir():
        shutil.copy2(img_path, dst_img / img_path.name)
        lbl_path = src_lbl / (img_path.stem + ".txt")
        if lbl_path.exists():
            shutil.copy2(lbl_path, dst_lbl / lbl_path.name)
        count += 1
    return count


def copy_baby_split(split: str) -> int:
    src_img = BABY_DIR / split / "images"
    src_lbl = BABY_DIR / split / "labels"
    dst_img = OUT_DIR / split / "images"
    dst_lbl = OUT_DIR / split / "labels"
    count = 0
    for img_path in src_img.iterdir():
        new_name = f"baby_{img_path.name}"
        shutil.copy2(img_path, dst_img / new_name)

        lbl_path = src_lbl / (img_path.stem + ".txt")
        new_lbl_path = dst_lbl / f"baby_{lbl_path.name}"
        if lbl_path.exists():
            lines = lbl_path.read_text().splitlines()
            remapped = []
            for line in lines:
                parts = line.split()
                if not parts:
                    continue
                parts[0] = str(PERSON_CLASS)
                remapped.append(" ".join(parts))
            new_lbl_path.write_text("\n".join(remapped) + ("\n" if remapped else ""))
        count += 1
    return count


def main():
    for split in SPLITS:
        (OUT_DIR / split / "images").mkdir(parents=True, exist_ok=True)
        (OUT_DIR / split / "labels").mkdir(parents=True, exist_ok=True)

        n_human = copy_human_split(split)
        n_baby = copy_baby_split(split)
        print(f"{split}: {n_human} human/human-like + {n_baby} baby = {n_human + n_baby} images")

    data_yaml = f"""train: ../train/images
val: ../valid/images
test: ../test/images

nc: 2
names: ['person', 'person-like']
"""
    (OUT_DIR / "data.yaml").write_text(data_yaml)
    print(f"\nWrote {OUT_DIR / 'data.yaml'}")


if __name__ == "__main__":
    main()
