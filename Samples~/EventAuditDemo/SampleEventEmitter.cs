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
        /// <summary>
        /// Parameterless interaction event used to test void listener scanning and cleanup.
        /// </summary>
        [Header("Scene Events to Scan with UnityEventAuditor")]
        public UnityEvent onInteracted = new UnityEvent();

        /// <summary>
        /// Integer-argument event used to test typed <see cref="UnityEvent{T0}"/> listener validation.
        /// </summary>
        public UnityEvent<int> onDamaged = new UnityEvent<int>();

        /// <summary>
        /// String-argument event used to test typed <see cref="UnityEvent{T0}"/> listener validation.
        /// </summary>
        public UnityEvent<string> onQuestUpdated = new UnityEvent<string>();

        /// <summary>
        /// Invokes all sample events with test values from the Inspector context menu.
        /// </summary>
        [ContextMenu("Trigger Sample Events")]
        public void TriggerAll()
        {
            onInteracted?.Invoke();
            onDamaged?.Invoke(10);
            onQuestUpdated?.Invoke("SampleQuestStep");
        }

        /// <summary>
        /// Sample parameterless callback target suitable for binding to <see cref="onInteracted"/>.
        /// </summary>
        public void LogInteraction()
        {
            Debug.Log($"[SampleEventEmitter] {name} received interaction callback.");
        }

        /// <summary>
        /// Sample integer callback target suitable for binding to <see cref="onDamaged"/>.
        /// </summary>
        /// <param name="amount">Damage value logged to the Unity Console.</param>
        public void LogDamage(int amount)
        {
            Debug.Log($"[SampleEventEmitter] {name} took {amount} damage.");
        }
    }
}
