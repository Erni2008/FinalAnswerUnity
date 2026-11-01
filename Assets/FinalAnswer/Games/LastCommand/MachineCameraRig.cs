using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
namespace FinalAnswer {
 public sealed class MachineCameraRig:MonoBehaviour {
  public Camera Camera;public MachineController Machine;public Vector3 Center=new Vector3(0,.37f,0);public float Radius=.55f;Vector3 target,smoothedTarget;float distance=1.65f,wantedDistance=1.65f,yaw=18,pitch=9,smoothYaw=18,smoothPitch=9;Vector2 previous,start;InteractiveMechanism captured,hover;float downAt;bool dragging,committed;Vector3 hitPoint;
  void Start(){target=smoothedTarget=Center;}
  public void Overview(){target=Center;wantedDistance=1.65f;yaw=18;pitch=9;Cancel();}
  public void Focus(Vector3 point,float zoom=.75f){target=point;wantedDistance=Mathf.Max(.58f,zoom);var offset=point-Center;if(Mathf.Abs(offset.x)>.22f)yaw=offset.x>0?75:-75;else if(offset.z<-.15f)yaw=180;else yaw=0;pitch=8;}
  public void Cancel(){captured?.Release();captured=null;committed=false;dragging=false;}
  void Update(){if(Mouse.current==null)return;var mouse=Mouse.current;Vector2 pos=mouse.position.ReadValue();bool ui=EventSystem.current&&EventSystem.current.IsPointerOverGameObject();var ray=Camera.ScreenPointToRay(pos);InteractiveMechanism nearest=null;if(!ui&&Physics.Raycast(ray,out var hit,10)){nearest=hit.collider.GetComponentInParent<InteractiveMechanism>();hitPoint=hit.point;}if(hover!=nearest){hover?.Highlight(false);hover=nearest;hover?.Highlight(true);}
   if(mouse.leftButton.wasPressedThisFrame&&!ui){start=previous=pos;captured=nearest;downAt=Time.unscaledTime;dragging=false;committed=false;if(captured is PressableButton button&&button.Available)button.Anticipate(true);}
   if(mouse.leftButton.isPressed&&!ui){var delta=pos-previous;if((pos-start).sqrMagnitude>64)dragging=true;
    if(captured is RotatableMechanism rotary){if(Mathf.Abs(pos.x-start.x)>26){rotary.Interact(Mathf.Sign(pos.x-start.x));start=pos;committed=true;}}
    else if(dragging){if(captured){captured.Release();captured=null;}yaw+=delta.x*.23f;pitch=Mathf.Clamp(pitch-delta.y*.23f,-35,65);}
    else if(!committed&&captured is PressableButton&&Time.unscaledTime-downAt>=.18f){captured.Interact();committed=true;}
   }
   if(mouse.leftButton.wasReleasedThisFrame){if(captured&&!dragging&&!committed){float side=pos.x-Camera.WorldToScreenPoint(captured.transform.position).x;captured.Interact(side<0?-1:1);}captured?.Release();captured=null;dragging=false;committed=false;}
   if(!ui){float scroll=mouse.scroll.ReadValue().y;if(scroll!=0)wantedDistance=Mathf.Clamp(wantedDistance*Mathf.Exp(-scroll*.001f),.6f,3f);if(mouse.rightButton.isPressed){var d=pos-previous;target+=Camera.transform.right*(-d.x*.0005f)+Camera.transform.up*(-d.y*.0005f);target=Center+Vector3.ClampMagnitude(target-Center,.4f);}}
   previous=pos;
  }
  void LateUpdate(){float t=1-Mathf.Exp(-Time.unscaledDeltaTime*10);smoothedTarget=Vector3.Lerp(smoothedTarget,target,t);distance=Mathf.Lerp(distance,wantedDistance,t);smoothYaw=Mathf.LerpAngle(smoothYaw,yaw,t);smoothPitch=Mathf.Lerp(smoothPitch,pitch,t);Vector3 direction=Quaternion.Euler(-smoothPitch,smoothYaw,0)*Vector3.forward;Vector3 position=smoothedTarget+direction*distance;Vector3 fromCenter=position-Center;if(fromCenter.magnitude<Radius*1.15f)position=Center+fromCenter.normalized*Radius*1.15f;position.y=Mathf.Max(.06f,position.y);Camera.transform.position=position;Camera.transform.LookAt(smoothedTarget);}
  void OnDisable()=>Cancel();
 }
}
