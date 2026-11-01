using UnityEngine;
namespace FinalAnswer {
 public sealed class PressableButton:InteractiveMechanism {
  public Vector3 Axis=Vector3.down;public float Travel=.01f;public float Depression{get;private set;}public bool Held{get;private set;}bool submitted;
  public void Anticipate(bool value){Held=value;if(!value)submitted=false;}
  public override void Interact(float direction=1){if(!Available||submitted)return;submitted=true;Held=true;Send("PRESS");}
  public override void Release(){if(submitted)Send("RELEASE");Held=false;submitted=false;}
  void Update(){float duration=Held?.1f:.15f;Depression=Mathf.MoveTowards(Depression,Held?1:0,Time.unscaledDeltaTime/duration);MovingPart.localPosition=restPosition+Axis.normalized*Travel*Mathf.SmoothStep(0,1,Depression);}
 }
}
