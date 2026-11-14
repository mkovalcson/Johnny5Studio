"""Inspect analytic CAD surfaces for recessed head fasteners (millimetres)."""
import collections
import json
import pathlib
import sys
from OCP.STEPControl import STEPControl_Reader
from OCP.TopAbs import TopAbs_FACE, TopAbs_REVERSED
from OCP.TopExp import TopExp_Explorer
from OCP.TopoDS import TopoDS
from OCP.BRepAdaptor import BRepAdaptor_Surface
from OCP.GeomAbs import GeomAbs_Cylinder
from OCP.BRepBndLib import BRepBndLib
from OCP.Bnd import Bnd_Box


def read_step(path):
    reader = STEPControl_Reader()
    if reader.ReadFile(str(path)) != 1:
        raise RuntimeError(f"Cannot read STEP: {path}")
    reader.TransferRoots()
    return reader.OneShape()


def cylinders(shape):
    explorer = TopExp_Explorer(shape, TopAbs_FACE)
    while explorer.More():
        face = TopoDS.Face_s(explorer.Current())
        surf = BRepAdaptor_Surface(face)
        if surf.GetType() == GeomAbs_Cylinder:
            cylinder = surf.Cylinder()
            axis = cylinder.Axis()
            yield {
                "radius": cylinder.Radius(),
                "center": list(axis.Location().Coord()),
                "axis": list(axis.Direction().Coord()),
                "v": [surf.FirstVParameter(), surf.LastVParameter()],
                "u": [surf.FirstUParameter(), surf.LastUParameter()],
                "inward": face.Orientation() == TopAbs_REVERSED,
            }
        explorer.Next()


if __name__ == "__main__":
    output = pathlib.Path(__file__).parent / "head-bolt-analysis.json"
    results = json.loads(output.read_text()) if output.exists() else {}
    for name in sys.argv[1:]:
        path = pathlib.Path(name)
        shape = read_step(path)
        box = Bnd_Box(); BRepBndLib.Add_s(shape, box)
        faces = list(cylinders(shape))
        print(path.name, "bounds", box.Get(), flush=True)
        print("Cylinders", collections.Counter((round(f["radius"], 4), f["inward"]) for f in faces), flush=True)
        results[path.name] = {"bounds": list(box.Get()), "cylinders": faces}
    output.write_text(json.dumps(results, indent=2))
