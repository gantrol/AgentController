from pathlib import Path

import bpy
from mathutils import Vector


ASSETS_DIR = Path(__file__).resolve().parent.parent
OUTPUT_BLEND = Path(__file__).resolve().parent / "controller-display.blend"
OUTPUT_PNG = ASSETS_DIR / "controller.png"

CONTROLLER_ROOT = "CREATRBOI White XBOX Controller (customized)"
CONTROLLER_MESH = "node_id30"


def world_bounds_center(obj: bpy.types.Object) -> Vector:
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return sum(points, Vector()) / len(points)


controller = bpy.data.objects[CONTROLLER_ROOT]
mesh = bpy.data.objects[CONTROLLER_MESH]
scene = bpy.context.scene
camera = scene.camera

if camera is None:
    raise RuntimeError("The controller scene does not have an active camera.")

# Raise the rear shoulder shelf toward the viewer. This keeps the face controls
# readable while exposing both trigger silhouettes and the bumper strip.
controller.rotation_euler.x = 0.46
bpy.context.view_layer.update()

target = world_bounds_center(mesh)
camera.location = target + Vector((0.0, -10.8, 7.5))
camera.rotation_euler = (
    target - camera.location
).to_track_quat("-Z", "Y").to_euler()
camera.data.lens = 55

scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1600
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.filepath = str(OUTPUT_PNG)

scene["agent_controller_view"] = "front-shoulder"
scene["agent_controller_view_notes"] = (
    "Front face preserved; LT/LB and RT/RB shoulder shelf exposed."
)

bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND))
bpy.ops.render.render(write_still=True)

print(f"Saved Blender source: {OUTPUT_BLEND}")
print(f"Rendered controller: {OUTPUT_PNG}")
