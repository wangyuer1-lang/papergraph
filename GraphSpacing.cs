using System.Windows;
namespace Papergraph;

public static class GraphSpacing
{
    public readonly record struct DragLimit(Point Origin,Point Fixed,double Distance);
    static Vector Direction(Vector delta,int salt=0){if(delta.Length>.00001)return delta/delta.Length;var angle=salt*2.399963;return new Vector(Math.Cos(angle),Math.Sin(angle));}
    public static Vector LimitDrag(Vector desired,Vector previous,IReadOnlyList<DragLimit> limits,Rect moving,Rect? boundary)
    {
        Vector Clamp(Vector value)=>boundary is Rect b?GraphBoard.ClampDelta(moving,b,value):value;
        var result=Clamp(desired);
        for(int pass=0;pass<20;pass++)
        {
            var before=result;foreach(var limit in limits)
            {
                var start=limit.Origin+previous-limit.Fixed;var step=result-previous;var length=step.LengthSquared;
                if(start.Length>=limit.Distance-.001&&length>.00001)
                {
                    var b=2*Vector.Multiply(start,step);var c=start.LengthSquared-limit.Distance*limit.Distance;var discriminant=b*b-4*length*c;
                    if(discriminant>=0){var t=(-b-Math.Sqrt(discriminant))/(2*length);if(t>=-.00001&&t<1){t=Math.Max(0,t);var outward=Direction(start+step*t);var remaining=step*(1-t);remaining-=outward*Math.Min(0,Vector.Multiply(remaining,outward));result=Clamp(previous+step*t+remaining);}}
                }
                var delta=limit.Origin+result-limit.Fixed;if(delta.Length<limit.Distance){var direction=delta.Length>.00001?Direction(delta):Direction(start);result=Clamp(result+direction*(limit.Distance-delta.Length));}
            }
            if((result-before).Length<.001)break;
        }
        // If several fixed neighbors or a board wall make the target impossible, retain the
        // last legal placement. Older crowded graphs can still be dragged out of overlap.
        foreach(var limit in limits){var oldDistance=(limit.Origin+previous-limit.Fixed).Length;var distance=(limit.Origin+result-limit.Fixed).Length;if(distance<Math.Min(limit.Distance,oldDistance)-.05)return previous;}
        return result;
    }
    public static void Separate(GraphRelaxation.Body[] bodies,Point[] positions,(int A,int B)[] links,CancellationToken cancellation=default)
    {
        Point Clamp(int i,Point p)=>bodies[i].Bounds is Rect b?GraphBoard.Clamp(p,b,bodies[i].Radius+5):p;
        for(int pass=0;pass<64;pass++)
        {
            cancellation.ThrowIfCancellationRequested();double change=0;
            foreach(var (a,b) in links)
            {
                if(!bodies[a].Movable&&!bodies[b].Movable)continue;double minimum=GraphStyle.MinimumNodeDistance(bodies[a].Radius,bodies[b].Radius);var oldA=positions[a];var oldB=positions[b];var delta=oldB-oldA;var deficit=minimum-delta.Length;if(deficit<.02)continue;var direction=Direction(delta,a*17+b*31);
                if(bodies[a].Movable)positions[a]=Clamp(a,oldA-direction*deficit*(bodies[b].Movable?.5:1));
                if(bodies[b].Movable)positions[b]=Clamp(b,oldB+direction*deficit*(bodies[a].Movable?.5:1));
                var remaining=minimum-(positions[b]-positions[a]).Length;
                if(remaining>.02&&bodies[a].Movable)positions[a]=Clamp(a,positions[a]-direction*remaining);
                remaining=minimum-(positions[b]-positions[a]).Length;
                if(remaining>.02&&bodies[b].Movable)positions[b]=Clamp(b,positions[b]+direction*remaining);
                if((positions[b]-positions[a]).Length<minimum-.05)
                {
                    double best=double.MaxValue;var bestA=positions[a];var bestB=positions[b];
                    void Consider(Point p,Point q){if((q-p).Length<minimum-.05)return;var cost=(p-oldA).LengthSquared+(q-oldB).LengthSquared;if(cost<best){best=cost;bestA=p;bestB=q;}}
                    for(int k=0;k<24;k++)
                    {
                        var unit=new Vector(Math.Cos(k*Math.PI/12),Math.Sin(k*Math.PI/12));
                        if(bodies[a].Movable)Consider(Clamp(a,positions[b]-unit*minimum),positions[b]);
                        if(bodies[b].Movable)Consider(positions[a],Clamp(b,positions[a]+unit*minimum));
                        if(bodies[a].Movable&&bodies[b].Movable){var center=oldA+(oldB-oldA)*.5;Consider(Clamp(a,center-unit*minimum*.5),Clamp(b,center+unit*minimum*.5));}
                    }
                    if(best<double.MaxValue){positions[a]=bestA;positions[b]=bestB;}
                }
                change=Math.Max(change,Math.Max((positions[a]-oldA).Length,(positions[b]-oldB).Length));
            }
            if(change<.01)break;
        }
    }
}
