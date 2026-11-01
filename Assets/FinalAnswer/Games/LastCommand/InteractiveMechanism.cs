using UnityEngine;
namespace FinalAnswer {
 public abstract class InteractiveMechanism:MonoBehaviour {
  public string Id;public Transform MovingPart;public bool Available=true;public MachineController Owner;protected Vector3 restPosition;protected Quaternion restRotation;MaterialPropertyBlock block;Renderer[] renderers;bool highlighted;
  protected virtual void Awake(){if(!MovingPart)MovingPart=transform;restPosition=MovingPart.localPosition;restRotation=MovingPart.localRotation;renderers=GetComponentsInChildren<Renderer>(true);block=new MaterialPropertyBlock();}
  public virtual void Interact(float direction=1){}
  public virtual void Release(){}
  Light selectionGlow;
  public void Highlight(bool on){if(highlighted==on)return;highlighted=on;if(!selectionGlow){var go=new GameObject("Selection highlight");go.transform.SetParent(transform,false);selectionGlow=go.AddComponent<Light>();selectionGlow.type=LightType.Point;selectionGlow.color=new Color(1,.58f,.16f);selectionGlow.range=.17f;selectionGlow.intensity=.18f;selectionGlow.shadows=LightShadows.None;}if(on&&renderers.Length>0){var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);selectionGlow.transform.position=bounds.center+(Camera.main?Camera.main.transform.position-bounds.center:Vector3.forward).normalized*.045f;}selectionGlow.enabled=on;}
  protected bool Send(string action,string value="")=>Owner&&Owner.Dispatch(this,action,value);
 }
}
