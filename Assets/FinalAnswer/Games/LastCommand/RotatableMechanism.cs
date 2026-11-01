using UnityEngine;
namespace FinalAnswer {
 public sealed class RotatableMechanism:InteractiveMechanism {
  public Vector3 Axis=Vector3.up;public float Degrees{get;private set;}public float Target{get;private set;}
  public void SetAngle(float angle){Target=Mathf.Repeat(angle,180);}
  public override void Interact(float direction=1){if(!Available)return;SetAngle(Target+(direction<0?-45:45));Send("STEP",direction<0?"-1":"+1");}
  void Update(){Degrees=Mathf.MoveTowardsAngle(Degrees,Target,Time.unscaledDeltaTime*360);MovingPart.localRotation=restRotation*Quaternion.AngleAxis(-Degrees,Axis);}
 }
}
