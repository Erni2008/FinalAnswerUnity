using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace FinalAnswer {
 public sealed class ImmediatePress:MonoBehaviour,IPointerDownHandler {public Action Accepted;public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left&&GetComponent<Button>().IsInteractable())Accepted?.Invoke();}}
}
