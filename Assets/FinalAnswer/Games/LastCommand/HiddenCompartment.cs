using UnityEngine;
using TMPro;
namespace FinalAnswer {
 public sealed class HiddenCompartment:InteractiveMechanism {
  public Transform Door;public GameObject Note;public TMP_Text NoteText;public bool Open{get;private set;}public float Angle{get;private set;}public string PlacementId="right";Quaternion doorRest;bool wasClosed=true;string hint="";
  protected override void Awake(){base.Awake();doorRest=Door.localRotation;if(Note)Note.SetActive(false);}
  public void SetOpen(bool value,string text=""){Open=value;hint=text;if(NoteText)NoteText.text=text;if(!value&&Note)Note.SetActive(false);}
  public override void Interact(float direction=1){if(!Available)return;SetOpen(!Open,hint);Send("SET",Open?"OPEN":"CLOSED");if(Open)Owner?.CameraRig?.Focus(transform.position,.75f);}
  void Update(){Angle=Mathf.MoveTowards(Angle,Open?115:0,Time.unscaledDeltaTime*230);Door.localRotation=doorRest*Quaternion.AngleAxis(Angle,Vector3.up);if(Note)Note.SetActive(Open&&Angle>=92&&!string.IsNullOrEmpty(hint));bool closed=!Open&&Angle<.01f;if(closed&&!wasClosed)Owner?.App?.Audio.Play("HATCH_CLOSE","close:"+Time.frameCount);wasClosed=closed;}
 }
}
