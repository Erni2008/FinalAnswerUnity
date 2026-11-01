using UnityEngine;
namespace FinalAnswer {
 public sealed class SlidingPanel:MonoBehaviour {
  public Vector3 LocalAxis=Vector3.forward;public float Travel=.018f,Seconds=.18f;public bool Open=true;public float Amount{get;private set;}=1;Vector3 rest;
  void Awake()=>rest=transform.localPosition;
  public void SetOpen(bool value)=>Open=value;
  void Update(){Amount=Mathf.MoveTowards(Amount,Open?1:0,Time.unscaledDeltaTime/Mathf.Max(.01f,Seconds));transform.localPosition=rest-LocalAxis*(Travel*(1-Mathf.SmoothStep(0,1,Amount)));}
 }
}
