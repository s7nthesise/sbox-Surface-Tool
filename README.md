# sbox-Surface-Tool
https://sbox.game/locksake/surfacetools

Surface-snapping editor tool with bounding box awareness and simple quick-rotate.

# Features

## Surface Movement
- Grab the anchor point of objects to force them to snap along any surface when moving cursor.

## Bounding Box Aware
- Offsets objects so their bounding box sits flush against surfaces. Takes into account children of objects. Works for multi-selection of numerous objects as well.

## Axis Arrows
- Pretty standard. Includes the three-axis translation arrows for constrained movement.

## Quick Rotate
- CTRL+R to instantly rotate the selected object. Uses the Angle Step setting.

## Duplicate on Drag
- Hold Shift while dragging to clone the selected objects. Very useful for setting up clutter environments.

## Anchor Point Modes
- WorldPosition or bounding box center as the grabbing pivot point. Bounding box center for most use cases, but the option is there for rare occasions. Center accurately updates when utilizing multi-selection.

## Customization
- Edit size of anchor point and axis arrows. Default arrows are atrociously inconvenient due to being too close to center object and anchor point, so this fixes that.
- Hide collider scaling gizmo. Yeah, I couldn't figure how to hide these in the editor. Hide Gizmos doesn't hide them and they 10/10 angered me when trying to move objects due to always grabbing them.

## Keybinds
- Y - Activate Surface Move tool
- CTRL+R - Quick rotate selected object
- Shift+Drag - Clone and move
  
**Keybinds can be changed through Edit>Preferences>Editor Keybinds.**

## Suggestions? Open a request. I only made this simple tool because it makes map making significantly easier, so as ideas come along I'll implement whenever.
