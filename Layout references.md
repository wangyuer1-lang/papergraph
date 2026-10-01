# Graph layout

The native C# solver in `GraphRelaxation.cs` was informed by damping, collision avoidance and cooling in [d3-force](https://github.com/d3/d3-force), and by [Graphology's force layout](https://graphology.github.io/standard-library/layout-force.html). These JavaScript libraries are not bundled with the application.

Local changes activate a bounded neighborhood around new points or relation endpoints. Existing positions provide soft constraints, while manually dropped points stay pinned. The solver computes its iterations and minimum-distance constraints before applying the final result once, without a continuous attraction animation.

Explicit Arrange uses `GraphArrange.cs`, independently of the local force solver. It computes a background snapshot and partitions direct children by parent and exact frame membership. Rings are handled from the inside out, with compact internal placement and whole-ring positioning in their enclosing frame. Compact reading rows follow the existing directed graph, with layered and barycentric alternatives where space permits. Frame coordinates and membership are validated before accepting each local result. Cross-frame edges are excluded. Crowded partitions remain unchanged. Editing, dragging or changing boards cancels pending results. This is a deterministic geometric arrangement, not a semantic rewrite.

`GraphSpacing.cs` shares minimum connector spacing and drag contact constraints. `GraphCurve.cs` adds a short arc when a straight connector has insufficient screen space, without changing saved coordinates. Point radii depend only on proposition text, never notes.

References: [d3-force simulation](https://d3js.org/d3-force/simulation), [collision implementation](https://github.com/d3/d3-force/blob/main/src/collide.js).
