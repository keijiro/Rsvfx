using UnityEngine;
using UnityEngine.Experimental.VFX;

public class KeyToParameter : MonoBehaviour
{
    public KeyCode keyCode = KeyCode.Space;
    public bool invert = false;
    public string parameterName = "Parameter";
    public VisualEffect target = null;

    int _parameterID;

    void Start()
    {
        _parameterID = Shader.PropertyToID(parameterName);
    }

    void Update()
    {
        var v = (Input.GetKey(keyCode) ^ invert) ? 1.0f : 0.0f;
        target.SetFloat(_parameterID, v);
    }
}
