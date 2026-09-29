using System.Windows;
namespace Papergraph;

// Rank nearby positions instead of hiding labels. The grid keeps work local on large graphs.
internal sealed class CaptionLayout(Rect viewport)
{
    sealed record Obstacle(Rect Bounds,double Weight,Point? Start=null,Point? End=null,double Radius=0);
    readonly Dictionary<(int X,int Y),List<Obstacle>> cells=[];
    static IEnumerable<(int,int)> Keys(Rect bounds)
    {
        for(int x=(int)Math.Floor(bounds.Left/96);x<=(int)Math.Floor(bounds.Right/96);x++)
            for(int y=(int)Math.Floor(bounds.Top/96);y<=(int)Math.Floor(bounds.Bottom/96);y++)yield return(x,y);
    }
    void Add(Obstacle obstacle)
    {
        var bounds=obstacle.Bounds;bounds.Intersect(viewport);if(bounds.IsEmpty)return;
        foreach(var key in Keys(bounds)){if(!cells.TryGetValue(key,out var bucket))cells[key]=bucket=[];bucket.Add(obstacle);}
    }
    internal void AddCircle(Point center,double radius)=>Add(new(new Rect(center.X-radius,center.Y-radius,2*radius,2*radius),1000,center,null,radius));
    internal void AddLine(Point a,Point b,double thickness=2.5,double weight=160)
    {
        var bounds=new Rect(a,b);bounds.Inflate(thickness,thickness);Add(new(bounds,weight,a,b,thickness));
    }
    internal void AddTitle(GraphCaptions.Placement placement){var bounds=placement.Bounds;bounds.Inflate(3,3);Add(new(bounds,600));}
    internal double Score(GraphCaptions.Placement placement)
    {
        var bounds=placement.Bounds;var inside=bounds;inside.Intersect(viewport);
        double area=bounds.Width*bounds.Height,visibleArea=inside.IsEmpty?0:inside.Width*inside.Height;
        double cost=(area-visibleArea)*60;if(inside.IsEmpty)return cost+1e6;
        var inverse=placement.Transform;inverse.Invert();var local=placement.TextBounds;local.Inflate(2/placement.Scale,2/placement.Scale);
        var query=bounds;query.Inflate(5,5);query.Intersect(viewport);var seen=new HashSet<Obstacle>(ReferenceEqualityComparer.Instance);
        foreach(var key in Keys(query))if(cells.TryGetValue(key,out var bucket))foreach(var obstacle in bucket)
        {
            if(!query.IntersectsWith(obstacle.Bounds)||!seen.Add(obstacle))continue;
            if(obstacle.Start is Point a&&obstacle.End is Point b)
            {
                var padded=local;padded.Inflate(obstacle.Radius/placement.Scale,obstacle.Radius/placement.Scale);
                var length=CrossingLength(padded,inverse.Transform(a),inverse.Transform(b))*placement.Scale;
                if(length>0)cost+=obstacle.Weight*(1+length);
            }
            else if(obstacle.Start is Point center)
            {
                var point=inverse.Transform(center);var nearest=new Point(Math.Clamp(point.X,local.Left,local.Right),Math.Clamp(point.Y,local.Top,local.Bottom));
                var overlap=obstacle.Radius-(point-nearest).Length*placement.Scale;if(overlap>0)cost+=obstacle.Weight*(1+overlap);
            }
            else
            {
                var overlap=Rect.Intersect(bounds,obstacle.Bounds);if(!overlap.IsEmpty)cost+=obstacle.Weight*(1+overlap.Width*overlap.Height/20);
            }
        }
        return cost;
    }
    internal GraphCaptions.Candidate Choose(IEnumerable<GraphCaptions.Candidate> candidates,int? previous=null,bool allowReposition=true)
    {
        var options=candidates.ToArray();var retained=options.FirstOrDefault(c=>c.Slot==previous);
        // Camera changes never change the chosen side. Small score improvements are not worth a jump.
        double priorCost=retained==null?double.PositiveInfinity:Score(retained.Placement);
        if(retained!=null&&(!allowReposition||priorCost<=1200))return retained;
        GraphCaptions.Candidate? best=null;double bestScore=double.PositiveInfinity;
        foreach(var candidate in options)
        {
            var score=Score(candidate.Placement)+candidate.Preference;
            if(score<bestScore){best=candidate;bestScore=score;}
            if(score<=0)break;
        }
        if(retained!=null&&priorCost-bestScore<Math.Max(1600,priorCost*.45))return retained;
        return best??throw new InvalidOperationException("A title needs a placement candidate.");
    }
    static double CrossingLength(Rect rect,Point a,Point b)
    {
        double low=0,high=1;var delta=b-a;
        bool Clip(double p,double q){if(Math.Abs(p)<.000001)return q>=0;var t=q/p;if(p<0){if(t>high)return false;low=Math.Max(low,t);}else{if(t<low)return false;high=Math.Min(high,t);}return true;}
        return Clip(-delta.X,a.X-rect.Left)&&Clip(delta.X,rect.Right-a.X)&&Clip(-delta.Y,a.Y-rect.Top)&&Clip(delta.Y,rect.Bottom-a.Y)?Math.Max(0,high-low)*delta.Length:0;
    }
}
