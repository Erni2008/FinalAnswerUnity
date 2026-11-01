using UnityEngine;
namespace FinalAnswer {
 public sealed class MechanicalSwitch:InteractiveMechanism {
  public Vector3 Axis=Vector3.right;public float[] Angles={-32,0,32};public int Position{get;private set;}float angle;
  public void SetPosition(int value){Position=Mathf.Clamp(value,0,Angles.Length-1);}
  public override void Interact(float direction=1){if(!Available)return;SetPosition((Position+1)%Angles.Length);Send("SET",new[]{"UP","MIDDLE","DOWN"}[Position]);}
  void Update(){angle=Mathf.MoveTowards(angle,Angles[Position],Time.unscaledDeltaTime*240);MovingPart.localRotation=restRotation*Quaternion.AngleAxis(angle,Axis);}
 }
}
