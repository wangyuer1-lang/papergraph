using System.Windows;
namespace Papergraph;

// Computes a settled snapshot; intermediate simulation steps are never displayed.
public sealed class GraphRelaxation
{
    public readonly record struct Body(string Id,Point Position,double Radius,bool Movable,Rect? Bounds=null,bool Seed=false);
    readonly Body[] bodies;readonly Point[] origin;readonly Vector[] velocity;readonly (int A,int B)[] links;
    readonly bool local;int ticks,quiet;double alpha=1;
    public Point[] Positions {get;}
    public IReadOnlyList<Body> Bodies=>bodies;
    public bool Finished {get;private set;}
    public void Complete(CancellationToken cancellation=default){while(!Finished){cancellation.ThrowIfCancellationRequested();Step();}GraphSpacing.Separate(bodies,Positions,links,cancellation);}
    public GraphRelaxation(Body[] bodies,(string From,string To)[] edges,bool local=true)
    {
        this.bodies=bodies;this.local=local;Positions=bodies.Select(n=>n.Position).ToArray();origin=Positions.ToArray();velocity=new Vector[bodies.Length];var index=bodies.Select((n,i)=>(n.Id,i)).ToDictionary(n=>n.Id,n=>n.i);
        links=edges.Where(e=>index.ContainsKey(e.From)&&index.ContainsKey(e.To)&&e.From!=e.To).Select(e=>(index[e.From],index[e.To])).Distinct().ToArray();
    }
    public bool Step()
    {
        if(Finished)return false;var force=new Vector[bodies.Length];var degree=new int[bodies.Length];foreach(var (a,b) in links){degree[a]++;degree[b]++;}
        var cells=new Dictionary<(int,int),List<int>>();const double cellSize=240;
        for(int i=0;i<bodies.Length;i++){var p=Positions[i]+velocity[i];var key=((int)Math.Floor(p.X/cellSize),(int)Math.Floor(p.Y/cellSize));if(!cells.TryGetValue(key,out var cell))cells[key]=cell=[];cell.Add(i);}
        for(int i=0;i<bodies.Length;i++)
        {
            var p=Positions[i]+velocity[i];int cx=(int)Math.Floor(p.X/cellSize),cy=(int)Math.Floor(p.Y/cellSize);
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)if(cells.TryGetValue((cx+x,cy+y),out var cell))foreach(var j in cell)
            {
                if(j<=i||!bodies[i].Movable&&!bodies[j].Movable)continue;var delta=p-(Positions[j]+velocity[j]);var distance=delta.Length;
                if(distance<.001){var angle=(i*17+j*31)*2.399963;delta=new Vector(Math.Cos(angle),Math.Sin(angle));distance=1;}
                var spacing=bodies[i].Radius+bodies[j].Radius+24;var collision=Math.Max(0,spacing-distance)*.30;var repulsion=distance<190?Math.Min(1.4,320/(distance*distance))*alpha:0;var push=delta/distance*(collision+repulsion);
                if(bodies[i].Movable)force[i]+=push*(bodies[j].Movable?.5:1);if(bodies[j].Movable)force[j]-=push*(bodies[i].Movable?.5:1);
            }
        }
        foreach(var (a,b) in links)
        {
            var delta=Positions[b]-Positions[a];double distance=Math.Max(.01,delta.Length);var rest=115+bodies[a].Radius+bodies[b].Radius;var strength=.022*alpha/Math.Sqrt(Math.Max(1,Math.Min(degree[a],degree[b])));var pull=delta/distance*Math.Clamp((distance-rest)*strength,-4,4);
            if(bodies[a].Movable)force[a]+=pull;if(bodies[b].Movable)force[b]-=pull;
        }
        var center=bodies.Length==0?new Point():new Point(origin.Average(p=>p.X),origin.Average(p=>p.Y));double speed=0;
        for(int i=0;i<bodies.Length;i++)
        {
            if(!bodies[i].Movable)continue;
            force[i]+=(local?origin[i]-Positions[i]:center-Positions[i])*(local?(bodies[i].Seed?.009:.045):.003)*alpha;
            velocity[i]=(velocity[i]+force[i])*.70;var length=velocity[i].Length;var cap=local?4:7;if(length>cap)velocity[i]*=cap/length;
            var next=Positions[i]+velocity[i];if(local){var displacement=next-origin[i];var limit=bodies[i].Seed?115:42;if(displacement.Length>limit)next=origin[i]+displacement/displacement.Length*limit;}
            if(bodies[i].Bounds is Rect bounds)next=GraphBoard.Clamp(next,bounds,bodies[i].Radius+5);
            speed=Math.Max(speed,(next-Positions[i]).Length);Positions[i]=next;
        }
        alpha*=local?.954:.977;ticks++;quiet=speed<.035?quiet+1:0;Finished=ticks>=(local?100:240)||quiet>=9;return !Finished;
    }
}
