using System.Collections.Generic;
namespace FinalAnswer {
 public static class CircuitPresentation {
  // Public board reachability for illumination only. Answer validation remains Java-owned.
  public static int Powered(int[] cells){if(cells==null||cells.Length!=9||(cells[0]&8)==0)return 0;int mask=1;var queue=new Queue<int>();queue.Enqueue(0);int[] delta={-3,1,3,-1};while(queue.Count>0){int i=queue.Dequeue();for(int d=0;d<4;d++){int j=i+delta[d];if(j<0||j>=9||d==1&&i%3==2||d==3&&i%3==0||(mask&(1<<j))!=0)continue;if((cells[i]&(1<<d))!=0&&(cells[j]&(1<<((d+2)%4)))!=0){mask|=1<<j;queue.Enqueue(j);}}}return mask;}
 }
}
