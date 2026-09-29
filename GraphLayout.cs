using System.Windows;
namespace Papergraph;
public static class GraphLayout
{
    public static Dictionary<string,Point> Arrange((string Id,Point Center,double Radius)[] nodes,(string From,string To)[] edges)
    {
        var count=nodes.Length;if(count==0)return [];var index=nodes.Select((n,i)=>(n.Id,i)).ToDictionary(p=>p.Id,p=>p.i);var p=nodes.Select(n=>n.Center).ToArray();var velocity=new Vector[count];
        var center=new Point(p.Average(q=>q.X),p.Average(q=>q.Y));
        for(int i=0;i<count;i++)p[i]+=new Vector(Math.Cos(i*2.399)*3,Math.Sin(i*2.399)*3);
        var links=edges.Where(e=>index.ContainsKey(e.From)&&index.ContainsKey(e.To)).Select(e=>(A:index[e.From],B:index[e.To])).ToArray();
        for(int step=0;step<200;step++)
        {
            var force=new Vector[count];
            void Repel(int i,int j){var d=p[i]-p[j];double distance=d.Length;if(distance<.01){d=new Vector(Math.Cos(i+1),Math.Sin(i+1));distance=1;}var needed=nodes[i].Radius+nodes[j].Radius+60;var power=Math.Min(18,2400/(distance*distance))+Math.Max(0,needed-distance)*.09;var f=d/distance*power;force[i]+=f;force[j]-=f;}
            if(count<=350){for(int i=0;i<count;i++)for(int j=i+1;j<count;j++)Repel(i,j);}
            else
            {
                var cells=new Dictionary<(int,int),List<int>>();for(int i=0;i<count;i++){var cell=((int)Math.Floor(p[i].X/200),(int)Math.Floor(p[i].Y/200));if(!cells.TryGetValue(cell,out var list))cells[cell]=list=[];list.Add(i);}
                for(int i=0;i<count;i++){int x=(int)Math.Floor(p[i].X/200),y=(int)Math.Floor(p[i].Y/200);for(int a=-1;a<=1;a++)for(int b=-1;b<=1;b++)if(cells.TryGetValue((x+a,y+b),out var list))foreach(var j in list)if(j>i)Repel(i,j);}
            }
            foreach(var (a,b) in links){var d=p[b]-p[a];var distance=Math.Max(1,d.Length);var f=d/distance*(distance-165)*.028;force[a]+=f;force[b]-=f;}
            for(int i=0;i<count;i++){force[i]+=(center-p[i])*.001;velocity[i]=(velocity[i]+force[i])*.72;var max=step<160?9:3;var speed=velocity[i].Length;if(speed>max)velocity[i]*=max/speed;p[i]+=velocity[i];}
        }
        return nodes.Select((n,i)=>(n.Id,Point:p[i])).ToDictionary(n=>n.Id,n=>n.Point);
    }
}
