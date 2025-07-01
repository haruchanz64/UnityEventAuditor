using UnityEngine;
using UnityEngine.Events;

namespace UnityEventAuditor.Samples
{
    /// <summary>
    /// Example MonoBehaviour exposing multiple UnityEvents for testing UnityEventAuditor scanning,
    /// filtering, and broken-listener cleanup in scenes or prefabs.
    /// </summary>
    public sealed class SampleEventEmitter : MonoBehaviour
    {
        [Header("Scene Events to Scan with UnityEventAuditor")]
        public UnityEvent onInteracted = new UnityEvent();
        public UnityEvent<int> onDamaged = new UnityEvent<int>();
        public UnityEvent<string> onQuestUpdated = new UnityEvent<string>();

        [ContextMenu("Trigger Sample Events")]
        public void TriggerAll()
        {
            onInteracted?.Invoke();
            onDamaged?.Invoke(10);
            onQuestUpdated?.Invoke("SampleQuestStep");
        }

        public void LogInteraction()
        {
            Debug.Log($"[SampleEventEmitter] {name} received interaction callback.");
        }

        public void LogDamage(int amount)
        {
            Debug.Log($"[SampleEventEmitter] {name} took {amount} damage.");
        }
    }
}
