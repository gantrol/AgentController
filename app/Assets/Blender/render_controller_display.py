from collections import deque
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


ASSETS_DIR = Path(__file__).resolve().parent.parent
OUTPUT_BLEND = Path(__file__).resolve().parent / "controller-display.blend"
OUTPUT_PNG = ASSETS_DIR / "controller.png"

CONTROLLER_ROOT = "CREATRBOI White XBOX Controller (customized)"
CONTROLLER_MESH = "node_id30"
COVER_OBJECT = "Removable unbranded center-button cover"
GUIDE_LOGO_REMOVED_MARKER = "agent_controller_guide_logo_removed"


def world_bounds_center(obj: bpy.types.Object) -> Vector:
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return sum(points, Vector()) / len(points)


def connected_components(mesh: bpy.types.Mesh) -> list[set[int]]:
    adjacency = [set() for _ in mesh.vertices]
    for edge in mesh.edges:
        first, second = edge.vertices
        adjacency[first].add(second)
        adjacency[second].add(first)

    remaining = set(range(len(mesh.vertices)))
    components = []
    while remaining:
        seed = remaining.pop()
        component = {seed}
        queue = deque([seed])
        while queue:
            vertex_index = queue.popleft()
            for neighbor in adjacency[vertex_index]:
                if neighbor not in remaining:
                    continue
                remaining.remove(neighbor)
                component.add(neighbor)
                queue.append(neighbor)
        components.append(component)

    return components


def is_center_guide_logo(mesh: bpy.types.Mesh, component: set[int]) -> bool:
    """Identify the source model's isolated raised Xbox emblem mesh island."""
    if not 400 <= len(component) <= 600:
        return False

    points = [mesh.vertices[index].co for index in component]
    minimum = Vector(
        (
            min(point.x for point in points),
            min(point.y for point in points),
            min(point.z for point in points),
        )
    )
    maximum = Vector(
        (
            max(point.x for point in points),
            max(point.y for point in points),
            max(point.z for point in points),
        )
    )
    center = (minimum + maximum) / 2
    extent = maximum - minimum

    # The source is a single combined mesh, but the raised emblem is a
    # disconnected island centered over the intact circular Guide-button face.
    return (
        1.15 <= center.x <= 1.55
        and abs(center.y) <= 0.10
        and 8.65 <= center.z <= 8.95
        and 0.75 <= extent.x <= 1.05
        and 0.75 <= extent.y <= 1.05
        and 0.60 <= extent.z <= 0.90
    )


def remove_center_guide_logo(mesh: bpy.types.Mesh) -> None:
    candidates = [
        component
        for component in connected_components(mesh)
        if is_center_guide_logo(mesh, component)
    ]

    if not candidates:
        if mesh.get(GUIDE_LOGO_REMOVED_MARKER):
            return
        raise RuntimeError(
            "Could not identify the isolated center Guide-button logo geometry."
        )
    if len(candidates) != 1:
        raise RuntimeError(
            f"Expected one center Guide-button logo component; found {len(candidates)}."
        )

    logo_vertex_indices = candidates[0]
    editable_mesh = bmesh.new()
    editable_mesh.from_mesh(mesh)
    editable_mesh.verts.ensure_lookup_table()
    bmesh.ops.delete(
        editable_mesh,
        geom=[
            vertex
            for vertex in editable_mesh.verts
            if vertex.index in logo_vertex_indices
        ],
        context="VERTS",
    )
    editable_mesh.to_mesh(mesh)
    editable_mesh.free()
    mesh.update()
    mesh[GUIDE_LOGO_REMOVED_MARKER] = True
    print(
        "Removed isolated raised center Guide-button emblem "
        f"({len(logo_vertex_indices)} vertices)."
    )


controller = bpy.data.objects[CONTROLLER_ROOT]
mesh = bpy.data.objects[CONTROLLER_MESH]
scene = bpy.context.scene
camera = scene.camera

if camera is None:
    raise RuntimeError("The controller scene does not have an active camera.")

# The experimental cover did not fully hide the underlying branded control and
# became more conspicuous at the shoulder-forward display angle.
if cover := bpy.data.objects.get(COVER_OBJECT):
    bpy.data.objects.remove(cover, do_unlink=True)

# Keep the original button body and replace its branded face with the clean
# circular surface already present underneath the isolated raised emblem.
remove_center_guide_logo(mesh.data)

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
scene["agent_controller_center_guide"] = "unbranded circular button face"

bpy.context.preferences.filepaths.save_version = 0
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND))
bpy.ops.render.render(write_still=True)

print(f"Saved Blender source: {OUTPUT_BLEND}")
print(f"Rendered controller: {OUTPUT_PNG}")
