using UnityEngine;
using System.Collections;

class DelayedActivator : MonoBehaviour
{
    public GameObject target = null;
    public uint delay = 5;

    IEnumerator Start()
    {
        for (var i = 0u; i < delay; i++)
            yield return null;

        target.SetActive(true);
    }
}
