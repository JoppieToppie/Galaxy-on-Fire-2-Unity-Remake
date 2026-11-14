// LuaBehaviour.cs
// A component whose behaviour is a mod script's Lua table (game.behaviour(object, table), ModScripts): Unity's messages
// are handed to the table's functions of these names, each called with the table itself first:
//   update(dt), lateUpdate(dt), fixedUpdate(dt), enabled(), disabled(), destroyed(),
//   triggerEnter(collider), triggerExit(collider), collisionEnter(collision)
// The script's state removes it when the scripts stop (Detach + Destroy); a message that keeps failing removes it too.

using System;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public class LuaBehaviour : MonoBehaviour
    {
        Action<string, object[]> call;
        readonly object[] one = new object[1];

        /// <summary>How often in a row a message of this behaviour failed (ModScripts removes it after a few).</summary>
        public int Failures { get; set; }

        /// <summary>The script's side: (message, arguments) -> the table's function of that name.</summary>
        public void Attach(Action<string, object[]> call)
        {
            this.call = call;
            if (isActiveAndEnabled) call("enabled", null);   // (AddComponent enabled it before the table was known)
        }

        public void Detach() => call = null;

        void Send(string message, object argument)
        {
            if (call == null) return;
            one[0] = argument;
            call(message, one);
        }

        void OnEnable() => call?.Invoke("enabled", null);
        void OnDisable() => call?.Invoke("disabled", null);
        void OnDestroy() { call?.Invoke("destroyed", null); call = null; }
        void Update() => Send("update", (double)Time.deltaTime);
        void LateUpdate() => Send("lateUpdate", (double)Time.deltaTime);
        void FixedUpdate() => Send("fixedUpdate", (double)Time.fixedDeltaTime);
        void OnTriggerEnter(Collider other) => Send("triggerEnter", other);
        void OnTriggerExit(Collider other) => Send("triggerExit", other);
        void OnCollisionEnter(Collision collision) => Send("collisionEnter", collision);
    }
}
