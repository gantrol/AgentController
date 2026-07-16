# Controller display source

`controller-display.blend` is the editable source for
`../controller.png`. The model is derived from the CREATRBOI White XBOX
Controller asset; attribution and license files are under
`app/ThirdParty/CREATRBOI-White-XBOX-Controller/`.

The display view intentionally tilts the controller forward so the normal face
controls remain readable while the left and right bumper/trigger silhouettes
are visible above the shell.

Render from the repository root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' `
  -b 'app\Assets\Blender\controller-display.blend' `
  -P 'app\Assets\Blender\render_controller_display.py'
```

The source model does not contain four rear paddles. Four-back-button
controllers are represented as a separate enhanced-controller capability in
the interaction specification rather than being implied by the main image.
