13
343597383682
197690942118438 1738446251876438800
{
  "name": "Kraken",
  "local_enabled": true,
  "local_position": {
    "X": -34.5189743041992188,
    "Y": 30.4965724945068359
  },
  "local_rotation": 0,
  "local_scale": {
    "X": 3,
    "Y": 3
  },
  "previous_sibling": "1734461834030333:1737998854061429500",
  "next_sibling": "789884796214014:1738769015564166200",
  "parent": "134953159748543:1738126268880005500",
  "spawn_as_networked_entity": true
},
{
  "cid": 1,
  "aoid": "197690942334215:1738446251876484100",
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
  "aoid": "197690942453548:1738446251876509500",
  "component_type": "Internal_Component",
  "internal_component_type": "Interactable",
  "data": {
    "text": "Fight Kraken (Lv. 25)",
    "radius": 4
  }
},
{
  "cid": 2,
  "aoid": "197877479183889:1738446291566129100",
  "component_type": "Mono_Component",
  "mono_component_type": "Boss",
  "data": {
    "Interactable": "197690942453548:1738446251876509500",
    "SpineAnimator": "197690942334215:1738446251876484100",
    "FishId": "Kraken",
    "Level": 25,
    "Type": "water"
  }
}
