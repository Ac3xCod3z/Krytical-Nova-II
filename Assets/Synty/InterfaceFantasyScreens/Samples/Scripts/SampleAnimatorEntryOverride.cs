using System.Collections.Generic;
using UnityEngine;

/// // Copyright (c) 2025 Synty Studios Limited. All rights reserved.
//
// Use of this software is subject to the terms and conditions of the End User Licence Agreement (EULA) 
// of the store at which you purchased this asset. 
//
// Synty assets are available at:
// https://www.syntystore.com
// https://assetstore.unity.com/publishers/5217
// https://www.fab.com/sellers/Synty%20Studios
//
// Sample scripts are included only as examples and are not intended as production-ready.

namespace Synty.Interface.FantasyScreens.Samples
{
[RequireComponent(typeof(Animator))]
public class AnimatorEntryOverride : MonoBehaviour
{
    [Tooltip("If true, the Animator stays disabled until SetEntryTrigger() is called, " +
             "guaranteeing your trigger (not the default Entry transition) decides the first state.")]
    [SerializeField] private bool waitForExplicitEntryTrigger = true;

    [Tooltip("Optional fallback trigger to apply automatically in Start() if nothing " +
             "called SetEntryTrigger() by then. Leave blank to just let the Animator's " +
             "own default Entry transition play instead.")]
    [SerializeField] private string defaultEntryTrigger = "";

    private Animator _animator;
    private readonly HashSet<int> _triggerHashes = new HashSet<int>();
    private bool _entryTriggerSet;

    /// <summary>Cached Animator reference, exposed in case other scripts need it.</summary>
    public Animator Animator => _animator;

    /// <summary>True once SetEntryTrigger() has been called on this instance.</summary>
    public bool EntryTriggerSet => _entryTriggerSet;

    private void Awake()
    {
        _animator = GetComponent<Animator>();

        foreach (var param in _animator.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger)
            {
                _triggerHashes.Add(param.nameHash);
            }
        }

        if (waitForExplicitEntryTrigger)
        {
            // Prevent the Entry transition from resolving until we say so.
            _animator.enabled = false;
        }
    }

    private void Start()
    {
        if (!waitForExplicitEntryTrigger || _entryTriggerSet)
        {
            return;
        }

        if (!string.IsNullOrEmpty(defaultEntryTrigger))
        {
            SetEntryTrigger(defaultEntryTrigger);
        }
        else
        {
            // No override requested - let the Animator's own default Entry transition play.
            _animator.enabled = true;
        }
    }

    /// <summary>
    /// Sets the given trigger and (if needed) enables the Animator so the trigger
    /// drives the Entry transition. Call this as early as possible - ideally right
    /// after instantiating the prefab, before Start() runs on this component.
    /// </summary>
    /// <param name="triggerName">Name of the Trigger parameter in the Animator Controller.</param>
    public void SetEntryTrigger(string triggerName)
    {
        if (_animator == null)
        {
            _animator = GetComponent<Animator>();
        }

        int hash = Animator.StringToHash(triggerName);

        if (_triggerHashes.Count > 0 && !_triggerHashes.Contains(hash))
        {
            Debug.LogWarning(
                $"[AnimatorEntryOverride] '{triggerName}' is not a Trigger parameter on the " +
                $"Animator Controller attached to '{gameObject.name}'. Check the spelling and " +
                "parameter type.", this);
        }

        // Clear any other triggers so only the intended one can satisfy an Entry transition.
        ClearOtherTriggers(hash);

        _animator.SetTrigger(hash);
        _entryTriggerSet = true;

        if (!_animator.enabled)
        {
            _animator.enabled = true;
        }
    }

    /// <summary>
    /// Resets every trigger parameter except the one specified, avoiding accidental
    /// double-triggers when switching between entry states.
    /// </summary>
    private void ClearOtherTriggers(int exceptHash)
    {
        foreach (var param in _animator.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger && param.nameHash != exceptHash)
            {
                _animator.ResetTrigger(param.nameHash);
            }
        }
    }
}
}
