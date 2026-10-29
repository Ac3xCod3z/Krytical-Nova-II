using System;
using System.Collections.Generic;
using KryticalNova.Core;

namespace KryticalNova.Persistence
{
    /// <summary>What <see cref="SaveParticipantSet.Load"/> found.</summary>
    public readonly struct SaveLoadResult
    {
        /// <summary>False when a part could not be brought up to this build; nothing was restored then and the save is untouched.</summary>
        public readonly bool Ok;
        public readonly string FailedKey;
        public readonly string FailedMessage;
        public readonly int Restored;
        public readonly int Passed;

        public SaveLoadResult(bool ok, string failedKey, string failedMessage, int restored, int passed)
        {
            Ok = ok;
            FailedKey = failedKey;
            FailedMessage = failedMessage;
            Restored = restored;
            Passed = passed;
        }
    }

    /// <summary>
    /// The two loops of <see cref="PlayerSaveAgent"/> (load and capture), pure and with a passthrough rule (M3a-2b, decision D3):
    /// a participant whose key the policy passes through is neither migrated, restored nor captured, so its stored text stays in the
    /// document exactly as loaded and the part is never dirty. Plain C#, tested with fake participants.
    /// </summary>
    public static class SaveParticipantSet
    {
        /// <summary>
        /// Steps every non-passthrough part up to this build's version, then restores each. A part that cannot be migrated
        /// (a failed step, or saved by a newer build) stops the load before anything is restored. One part's restore that throws
        /// does not cost the others.
        /// </summary>
        /// <param name="passthrough">True for a key to leave alone; null = none (the Netcode for GameObjects and offline bodies).</param>
        /// <param name="onRestoreError">Called with the key and the exception of a restore that threw.</param>
        public static SaveLoadResult Load(IReadOnlyList<ISaveParticipant> participants, AccountSave account, CharacterSave character,
            Func<string, bool> passthrough, Action<string, Exception> onRestoreError)
        {
            var upgraded = new string[participants.Count];
            var passed = new bool[participants.Count];
            int passedCount = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                ISaveParticipant participant = participants[i];
                if (passthrough != null && passthrough(participant.SaveKey))
                {
                    passed[i] = true;
                    passedCount++;
                    continue;
                }
                string stored = participant.SaveScope == SaveScope.Account ? account.Get(participant.SaveKey) : character.Get(participant.SaveKey);
                try
                {
                    upgraded[i] = participant is IMigratingSave migrating ? SaveVersions.Upgrade(stored, migrating) : stored;
                }
                catch (Exception e)
                {
                    return new SaveLoadResult(false, participant.SaveKey, e.Message, 0, passedCount);
                }
            }

            int restored = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                if (passed[i]) continue;
                try
                {
                    participants[i].RestoreSave(upgraded[i]);
                    restored++;
                }
                catch (Exception e)
                {
                    onRestoreError?.Invoke(participants[i].SaveKey, e);
                }
            }
            return new SaveLoadResult(true, null, null, restored, passedCount);
        }

        /// <summary>
        /// Gathers every non-passthrough participant's state into the documents; true when anything changed. A passthrough key is
        /// not even asked. A part whose capture throws keeps its last save.
        /// </summary>
        public static bool Capture(IReadOnlyList<ISaveParticipant> participants, AccountSave account, CharacterSave character,
            Func<string, bool> passthrough, Action<string, Exception> onCaptureError)
        {
            bool changed = false;
            for (int i = 0; i < participants.Count; i++)
            {
                ISaveParticipant participant = participants[i];
                if (passthrough != null && passthrough(participant.SaveKey)) continue;
                string json;
                try
                {
                    // Stamped with the part's version when it has one (save migrations).
                    json = SaveVersions.Stamped(participant.CaptureSave(), participant);
                }
                catch (Exception e)
                {
                    onCaptureError?.Invoke(participant.SaveKey, e);
                    continue;
                }
                changed |= participant.SaveScope == SaveScope.Account ? account.Set(participant.SaveKey, json) : character.Set(participant.SaveKey, json);
            }
            return changed;
        }
    }
}
