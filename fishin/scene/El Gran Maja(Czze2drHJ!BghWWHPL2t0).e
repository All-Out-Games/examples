13
360777252865
790788144804297 1738769207764413300
{
  "name": "El Gran Maja",
  "local_enabled": true,
  "local_position": {
    "X": 1.6390225887298584,
    "Y": 38.2497634887695312
  },
  "local_rotation": 0,
  "local_scale": {
    "X": 3,
    "Y": 3
  },
  "previous_sibling": "789884796214014:1738769015564166200",
  "next_sibling": "175056893539904:1738441436005790000",
  "parent": "134953159748543:1738126268880005500",
  "spawn_as_networked_entity": true
},
{
  "cid": 1,
  "aoid": "790788144996292:1738769207764453600",
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
  "aoid": "790788145109891:1738769207764477800",
  "component_type": "Internal_Component",
  "internal_component_type": "Interactable",
  "data": {
    "text": "Fight El Gran Maja (Lv. 75)",
    "radius": 4
  }
},
{
  "cid": 2,
  "aoid": "790788145144765:1738769207764485200",
  "component_type": "Mono_Component",
  "mono_component_type": "Boss",
  "data": {
    "Interactable": "790788145109891:1738769207764477800",
    "SpineAnimator": "790788144996292:1738769207764453600",
    "FishId": "El_Gran_Maja",
    "Level": 75,
    "Type": "grass"
  }
}
