# Graph layout

The native C# solver in `GraphRelaxation.cs` was informed by damping, collision avoidance and cooling in [d3-force](https://github.com/d3/d3-force), and by [Graphology's force layout](https://graphology.github.io/standard-library/layout-force.html). These JavaScript libraries are not bundled with the application.

Local changes activate a bounded neighborhood around new points or relation endpoints. Existing positions provide soft constraints, while manually dropped points stay pinned. The solver computes its iterations and minimum-distance constraints before applying the final result once, without a continuous attraction animation.

Arrange graph computes a snapshot in the background; editing, dragging or changing boards cancels pending results. □ contents stay within their geometric bounds. Intersection points are constrained by the intersection of their □; □ rectangles themselves do not enter the force simulation.

`GraphSpacing.cs` shares minimum connector spacing and drag contact constraints. `GraphCurve.cs` keeps single connectors straight and uses shallow lanes for parallel relations. Arrowheads adapt to the available gap without changing saved coordinates. Point radii depend only on proposition text, never notes.

References: [d3-force simulation](https://d3js.org/d3-force/simulation), [collision implementation](https://github.com/d3/d3-force/blob/main/src/collide.js).

