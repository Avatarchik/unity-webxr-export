using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.XR;
#endif
using System;
using System.Collections.Generic;

namespace WebXR
{

  public class WebXRController : MonoBehaviour
  {
    public enum ButtonTypes
    {
      Trigger = 0,
      Grip = 1,
      Thumbstick = 2,
      Touchpad = 3,
      ButtonA = 4,
      ButtonB = 5
    }

    public enum AxisTypes
    {
      Trigger,
      Grip
    }

    public enum Axis2DTypes
    {
      Thumbstick, // primary2DAxis
      Touchpad // secondary2DAxis
    }

    [Tooltip("Controller hand to use.")]
    public WebXRControllerHand hand = WebXRControllerHand.NONE;
    [Tooltip("Simulate 3dof controller")]
    public bool simulate3dof = false;
    [Tooltip("Vector from head to elbow")]
    public Vector3 eyesToElbow = new Vector3(0.1f, -0.4f, 0.15f);
    [Tooltip("Vector from elbow to hand")]
    public Vector3 elbowHand = new Vector3(0, 0, 0.25f);

    public Transform handJointPrefab;


    public GameObject[] showGOs;

    private Matrix4x4 sitStand;

    private float trigger;
    private float squeeze;
    private float thumbstick;
    private float thumbstickX;
    private float thumbstickY;
    private float touchpad;
    private float touchpadX;
    private float touchpadY;
    private float buttonA;
    private float buttonB;

    private Quaternion headRotation;
    private Vector3 headPosition;
    private Dictionary<ButtonTypes, WebXRControllerButton> buttonStates = new Dictionary<ButtonTypes, WebXRControllerButton>();

    private Dictionary<int, Transform> handJoints = new Dictionary<int, Transform>();
    private bool handJointsVisible = false;

#if UNITY_EDITOR
    private InputDeviceCharacteristics xrHand = InputDeviceCharacteristics.Controller;
    private InputDevice? inputDevice;
    private HapticCapabilities? hapticCapabilities;
#endif

    public void TryUpdateButtons()
    {
#if UNITY_EDITOR
      if (!WebXRManager.Instance.isSubsystemAvailable && inputDevice != null)
      {
        inputDevice.Value.TryGetFeatureValue(CommonUsages.trigger, out trigger);
        inputDevice.Value.TryGetFeatureValue(CommonUsages.grip, out squeeze);
        if (trigger <= 0.02)
        {
          trigger = 0;
        }
        else if (trigger >= 0.98)
        {
          trigger = 1;
        }

        if (squeeze <= 0.02)
        {
          squeeze = 0;
        }
        else if (squeeze >= 0.98)
        {
          squeeze = 1;
        }

        Vector2 axis2D;
        if (inputDevice.Value.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis2D))
        {
          thumbstickX = axis2D.x;
          thumbstickY = axis2D.y;
        }
        if (inputDevice.Value.TryGetFeatureValue(CommonUsages.secondary2DAxis, out axis2D))
        {
          touchpadX = axis2D.x;
          touchpadY = axis2D.y;
        }
        bool buttonPressed;
        if (inputDevice.Value.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out buttonPressed))
        {
          thumbstick = buttonPressed ? 1 : 0;
        }
        if (inputDevice.Value.TryGetFeatureValue(CommonUsages.secondary2DAxisClick, out buttonPressed))
        {
          touchpad = buttonPressed ? 1 : 0;
        }
        if (inputDevice.Value.TryGetFeatureValue(CommonUsages.primaryButton, out buttonPressed))
        {
          buttonA = buttonPressed ? 1 : 0;
        }
        if (inputDevice.Value.TryGetFeatureValue(CommonUsages.secondaryButton, out buttonPressed))
        {
          buttonB = buttonPressed ? 1 : 0;
        }

        WebXRControllerButton[] buttons = new WebXRControllerButton[6];
        buttons[(int)ButtonTypes.Trigger] = new WebXRControllerButton(trigger == 1, trigger);
        buttons[(int)ButtonTypes.Grip] = new WebXRControllerButton(squeeze == 1, squeeze);
        buttons[(int)ButtonTypes.Thumbstick] = new WebXRControllerButton(thumbstick == 1, thumbstick);
        buttons[(int)ButtonTypes.Touchpad] = new WebXRControllerButton(touchpad == 1, touchpad);
        buttons[(int)ButtonTypes.ButtonA] = new WebXRControllerButton(buttonA == 1, buttonA);
        buttons[(int)ButtonTypes.ButtonB] = new WebXRControllerButton(buttonB == 1, buttonB);
        UpdateButtons(buttons);
      }
#endif
    }

    // Updates button states from Web gamepad API.
    private void UpdateButtons(WebXRControllerButton[] buttons)
    {
      for (int i = 0; i < buttons.Length; i++)
      {
        WebXRControllerButton button = buttons[i];
        SetButtonState((ButtonTypes)i, button.pressed, button.value);
      }
    }

    public float GetAxis(AxisTypes action)
    {
      switch (action)
      {
        case AxisTypes.Grip:
          return squeeze;
        case AxisTypes.Trigger:
          return trigger;
      }
      return 0;
    }

    public Vector2 GetAxis2D(Axis2DTypes action)
    {
      switch (action)
      {
        case Axis2DTypes.Thumbstick:
          return new Vector2(thumbstickX, thumbstickY);
        case Axis2DTypes.Touchpad:
          return new Vector2(touchpadX, touchpadY);
      }
      return Vector2.zero;
    }

    public bool GetButton(ButtonTypes action)
    {
      if (!buttonStates.ContainsKey(action))
      {
        return false;
      }
      return buttonStates[action].pressed;
    }

    private bool GetPastButtonState(ButtonTypes action)
    {
      if (!buttonStates.ContainsKey(action))
        return false;
      return buttonStates[action].prevPressedState;
    }

    private void SetButtonState(ButtonTypes action, bool isPressed, float value)
    {
      if (buttonStates.ContainsKey(action))
      {
        buttonStates[action].pressed = isPressed;
        buttonStates[action].value = value;
      }
      else
        buttonStates.Add(action, new WebXRControllerButton(isPressed, value));
    }

    private void SetPastButtonState(ButtonTypes action, bool isPressed)
    {
      if (!buttonStates.ContainsKey(action))
        return;
      buttonStates[action].prevPressedState = isPressed;
    }

    public bool GetButtonDown(ButtonTypes action)
    {
      if (GetButton(action) && !GetPastButtonState(action))
      {
        SetPastButtonState(action, true);
        return true;
      }
      return false;
    }

    public bool GetButtonUp(ButtonTypes action)
    {
      if (!GetButton(action) && GetPastButtonState(action))
      {
        SetPastButtonState(action, false);
        return true;
      }
      return false;
    }

    private void onHeadsetUpdate(Matrix4x4 leftProjectionMatrix,
        Matrix4x4 rightProjectionMatrix,
        Matrix4x4 leftViewMatrix,
        Matrix4x4 rightViewMatrix,
        Matrix4x4 sitStandMatrix)
    {
      Matrix4x4 trs = WebXRMatrixUtil.TransformViewMatrixToTRS(leftViewMatrix);
      this.headRotation = WebXRMatrixUtil.GetRotationFromMatrix(trs);
      this.headPosition = WebXRMatrixUtil.GetTranslationFromMatrix(trs);
      this.sitStand = sitStandMatrix;
    }

    private void OnControllerUpdate(WebXRControllerData controllerData)
    {
      if (controllerData.hand == (int)hand)
      {
        if (!controllerData.enabled)
        {
          SetVisible(false);
          return;
        }
        SetVisible(true);

        transform.localRotation = controllerData.rotation;
        transform.localPosition = controllerData.position;

        trigger = controllerData.trigger;
        squeeze = controllerData.squeeze;
        thumbstick = controllerData.thumbstick;
        thumbstickX = controllerData.thumbstickX;
        thumbstickY = controllerData.thumbstickY;
        touchpad = controllerData.touchpad;
        touchpadX = controllerData.touchpadX;
        touchpadY = controllerData.touchpadY;
        buttonA = controllerData.buttonA;
        buttonB = controllerData.buttonB;

        WebXRControllerButton[] buttons = new WebXRControllerButton[6];
        buttons[(int)ButtonTypes.Trigger] = new WebXRControllerButton(trigger == 1, trigger);
        buttons[(int)ButtonTypes.Grip] = new WebXRControllerButton(squeeze == 1, squeeze);
        buttons[(int)ButtonTypes.Thumbstick] = new WebXRControllerButton(thumbstick == 1, thumbstick);
        buttons[(int)ButtonTypes.Touchpad] = new WebXRControllerButton(touchpad == 1, touchpad);
        buttons[(int)ButtonTypes.ButtonA] = new WebXRControllerButton(buttonA == 1, buttonA);
        buttons[(int)ButtonTypes.ButtonB] = new WebXRControllerButton(buttonB == 1, buttonB);
        UpdateButtons(buttons);
      }
    }

    private void OnHandUpdate(WebXRHandData handData)
    {
      if (handData.hand == (int)hand)
      {
        if (!handData.enabled)
        {
          SetHandJointsVisible(false);
          return;
        }
        SetVisible(false);
        SetHandJointsVisible(true);

        transform.localPosition = handData.joints[0].position;
        transform.localRotation = handData.joints[0].rotation;

        Quaternion rotationOffset = Quaternion.Inverse(handData.joints[0].rotation);

        for (int i = 0; i <= WebXRHandData.LITTLE_PHALANX_TIP; i++)
        {
          if (handData.joints[i].enabled)
          {
            if (handJoints.ContainsKey(i))
            {
              handJoints[i].localPosition = rotationOffset * (handData.joints[i].position - handData.joints[0].position);
              handJoints[i].localRotation = rotationOffset * handData.joints[i].rotation;
            }
            else
            {
              var clone = Instantiate(handJointPrefab,
                                      rotationOffset * (handData.joints[i].position - handData.joints[0].position),
                                      rotationOffset * handData.joints[i].rotation,
                                      transform);
              if (handData.joints[i].radius > 0f)
              {
                clone.localScale = new Vector3(handData.joints[i].radius, handData.joints[i].radius, handData.joints[i].radius);
              }
              else
              {
                clone.localScale = new Vector3(0.005f, 0.005f, 0.005f);
              }
              handJoints.Add(i, clone);
            }
          }
        }

        trigger = handData.trigger;
        squeeze = handData.squeeze;

        WebXRControllerButton[] buttons = new WebXRControllerButton[2];
        buttons[(int)ButtonTypes.Trigger] = new WebXRControllerButton(trigger == 1, trigger);
        buttons[(int)ButtonTypes.Grip] = new WebXRControllerButton(squeeze == 1, squeeze);
        UpdateButtons(buttons);
      }
    }

    private WebXRControllerHand handFromString(string handValue)
    {
      WebXRControllerHand handParsed = WebXRControllerHand.NONE;

      if (!String.IsNullOrEmpty(handValue))
      {
        try
        {
          handParsed = (WebXRControllerHand)Enum.Parse(typeof(WebXRControllerHand), handValue.ToUpper(), true);
        }
        catch
        {
          Debug.LogError("Unrecognized controller Hand '" + handValue + "'!");
        }
      }
      return handParsed;
    }

    private void SetVisible(bool visible)
    {
      foreach (var showGO in showGOs)
      {
        showGO.SetActive(visible);
      }
    }

    private void SetHandJointsVisible(bool visible)
    {
      if (handJointsVisible == visible)
      {
        return;
      }
      handJointsVisible = visible;
      foreach (var handJoint in handJoints)
      {
        handJoint.Value.gameObject.SetActive(visible);
      }
    }

    // intensity 0 to 1, duration milliseconds
    public void Pulse(float intensity, float duration)
    {
      if (WebXRManager.Instance.isSubsystemAvailable)
      {
        WebXRManager.Instance.HapticPulse(hand, intensity, duration);
      }
#if UNITY_EDITOR
      else if (inputDevice != null && hapticCapabilities != null
               && hapticCapabilities.Value.supportsImpulse)
      {
        // duration in seconds
        duration = duration * 0.001f;
        inputDevice.Value.SendHapticImpulse(0, intensity, duration);
      }
#endif
    }

    void OnEnable()
    {
      WebXRManager.OnControllerUpdate += OnControllerUpdate;
      WebXRManager.OnHandUpdate += OnHandUpdate;
      WebXRManager.OnHeadsetUpdate += onHeadsetUpdate;
      SetVisible(false);
#if UNITY_EDITOR
      switch (hand)
      {
        case WebXRControllerHand.LEFT:
          xrHand = InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left;
          break;
        case WebXRControllerHand.RIGHT:
          xrHand = InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right;
          break;
      }

      List<InputDevice> allDevices = new List<InputDevice>();
      InputDevices.GetDevicesWithCharacteristics(xrHand, allDevices);
      foreach (InputDevice device in allDevices)
      {
        HandleInputDevicesConnected(device);
      }

      InputDevices.deviceConnected += HandleInputDevicesConnected;
      InputDevices.deviceDisconnected += HandleInputDevicesDisconnected;
#endif
    }

    void OnDisabled()
    {
      WebXRManager.OnControllerUpdate -= OnControllerUpdate;
      WebXRManager.OnHandUpdate -= OnHandUpdate;
      WebXRManager.OnHeadsetUpdate -= onHeadsetUpdate;
      SetVisible(false);
#if UNITY_EDITOR
      InputDevices.deviceConnected -= HandleInputDevicesConnected;
      InputDevices.deviceDisconnected -= HandleInputDevicesDisconnected;
      inputDevice = null;
#endif
    }

#if UNITY_EDITOR
    private void HandleInputDevicesConnected(InputDevice device)
    {
      if (device.characteristics.HasFlag(xrHand))
      {
        inputDevice = device;
        HapticCapabilities capabilities;
        if (device.TryGetHapticCapabilities(out capabilities))
        {
          hapticCapabilities = capabilities;
        }
        SetVisible(true);
      }
    }

    private void HandleInputDevicesDisconnected(InputDevice device)
    {
      if (inputDevice != null && inputDevice.Value == device)
      {
        inputDevice = null;
        hapticCapabilities = null;
        SetVisible(false);
      }
    }
#endif
  }
}
