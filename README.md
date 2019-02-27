Rsvfx
-----

**Rsvfx** is an example that shows how to connect an Intel [RealSense] depth
camera to a Unity [Visual Effect Graph].

![gif](https://i.imgur.com/K0C80Lf.gif)
![gif](https://i.imgur.com/jBxII0t.gif)

[RealSense]: https://realsense.intel.com/
[Visual Effect Graph]: https://unity.com/visual-effect-graph

System requirements
-------------------

- Unity 2018.3 or later
- Intel RealSense D400 series

This repository only contains Windows and Linux versions of the RealSense
plugin binaries. For macOS, it has to be installed separately.


How it works
------------

![inspector](https://i.imgur.com/JWEUhXh.png)

The [PointCloudBaker] component converts a point cloud stream sent from a
RealSense device into two dynamically animated attribute maps: position map and
color map. These maps can be used in the "Set Position/Color from Map" blocks
in a VFX graph, in the same way as attribute maps imported from a point cache
file.

![blocks](https://i.imgur.com/mEY3I2d.png)

[PointCloudBaker]: /Assets/Rsvfx/PointCloudBaker.cs
