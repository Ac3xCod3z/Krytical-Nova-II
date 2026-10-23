"""The FBX export settings proven for Sidekick parts (same as sidekick_export.py; see the spec's Export settings)."""
EXPORT = dict(use_selection=True, object_types={"ARMATURE", "MESH"}, global_scale=1.0, apply_unit_scale=True,
              apply_scale_options="FBX_SCALE_NONE", use_space_transform=True, bake_space_transform=False,
              axis_forward="-Z", axis_up="Y", add_leaf_bones=False, primary_bone_axis="Y",
              secondary_bone_axis="X", use_armature_deform_only=False, armature_nodetype="NULL",
              mesh_smooth_type="OFF", use_tspace=False, bake_anim=False, use_mesh_modifiers=False,
              use_custom_props=False, path_mode="AUTO", embed_textures=False)
