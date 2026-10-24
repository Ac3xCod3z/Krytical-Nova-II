using System;
using System.IO;
using KryticalNova.NPC;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Gives every town NPC its spoken lines (owner, 24 Oct 2026): what it says when a player walks up, in the voice
    /// the owner picked, one line each visit and never the same twice in a row. The lines and clips come from
    /// <c>Assets/_Game/Audio/Voices/NPC/npc_voice_lines.json</c>, which the npc-voice skill's bake_game.py writes
    /// beside the clips (every clip of one NPC is the same voice: cloned from one sample and speaker-checked).
    /// Also makes the mixer's Voice group (the Voices slider) and sets the clips' import settings. Safe to run again.
    /// </summary>
    public static class NpcVoiceSetup
    {
        public const string VoiceFolder = "Assets/_Game/Audio/Voices/NPC";
        private const string Manifest = VoiceFolder + "/npc_voice_lines.json";
        private const string NpcDataFolder = "Assets/_Game/Gameplay/NPC/Data/Assets";

        [Serializable] private class ManifestLine { public string text; public string clip; }
        [Serializable] private class ManifestNpc { public string npc; public string asset; public string voice; public ManifestLine[] lines; }
        [Serializable] private class ManifestFile { public ManifestNpc[] npcs; }

        [MenuItem("Krytical Nova/Setup/NPC Voice Lines")]
        public static void Apply()
        {
            if (AudioSetup.VoiceOutput() == null)
                Debug.LogWarning("[NPC Voices] The mixer has no Voice group; lines will follow the Master slider only.");
            if (!File.Exists(Manifest))
            {
                Debug.LogError($"[NPC Voices] {Manifest} is missing: bake the picked lines with the npc-voice skill first.");
                return;
            }
            int clips = ImportSettings();
            var file = JsonUtility.FromJson<ManifestFile>(File.ReadAllText(Manifest));
            int npcs = 0, lines = 0, missing = 0;
            foreach (ManifestNpc entry in file.npcs ?? new ManifestNpc[0])
            {
                var definition = AssetDatabase.LoadAssetAtPath<NpcDefinition>($"{NpcDataFolder}/{entry.asset}.asset");
                if (definition == null)
                {
                    Debug.LogWarning($"[NPC Voices] No NPC asset {entry.asset} for {entry.npc}.");
                    continue;
                }
                var result = new NpcLine[entry.lines?.Length ?? 0];
                for (int i = 0; i < result.Length; i++)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(entry.lines[i].clip);
                    if (clip == null) missing++;
                    result[i] = new NpcLine { text = entry.lines[i].text, clip = clip };
                }
                definition.lines = result;
                EditorUtility.SetDirty(definition);
                npcs++;
                lines += result.Length;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[NPC Voices] {npcs} NPC(s) have {lines} spoken line(s); {clips} clip(s) had their import settings set" +
                      (missing > 0 ? $"; {missing} clip(s) were not found." : "."));
        }

        /// <summary>Voice lines: mono, Vorbis, loaded when an NPC first speaks rather than with the scene.</summary>
        private static int ImportSettings()
        {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { VoiceFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                bool same = importer.forceToMono && settings.loadType == AudioClipLoadType.CompressedInMemory &&
                            settings.compressionFormat == AudioCompressionFormat.Vorbis && !settings.preloadAudioData;
                if (same) continue;
                importer.forceToMono = true;
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.6f;
                settings.preloadAudioData = false;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                changed++;
            }
            return changed;
        }
    }
}
