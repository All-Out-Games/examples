13
472446402561
1731438361483178 1741290288165765000
{
  "name": "Bloop",
  "local_enabled": true,
  "local_position": {
    "X": 47.4729690551757812,
    "Y": -31.5387687683105469
  },
  "local_rotation": 0,
  "local_scale": {
    "X": 3,
    "Y": 3
  },
  "previous_sibling": "108761665319518:1739555827072821600",
  "parent": "134953159748543:1738126268880005500",
  "spawn_as_networked_entity": true
},
{
  "cid": 1,
  "aoid": "1731438361689884:1741290288165808500",
  "component_type": "Internal_Component",
  "internal_component_type": "Spine_Animator",
  "data": {
    "skeleton_data_asset": "Animations/bubbles_rise/bubbling_water.spine",
    "ordered_skins": [
      "default"
    ],
    "depth_offset": -111
  }
},
{
  "cid": 3,
  "aoid": "1731438361826090:1741290288165837500",
  "component_type": "Internal_Component",
  "internal_component_type": "Interactable",
  "data": {
    "text": "Fight Bloop (Lv. 80)",
    "radius": 4
  }
},
{
  "cid": 2,
  "aoid": "1731438361874312:1741290288165847800",
  "component_type": "Mono_Component",
  "mono_component_type": "Boss",
  "data": {
    "Interactable": "1731438361826090:1741290288165837500",
    "SpineAnimator": "1731438361689884:1741290288165808500",
    "FishId": "Bloop",
    "Level": 80,
    "Type": "water"
  }
}
