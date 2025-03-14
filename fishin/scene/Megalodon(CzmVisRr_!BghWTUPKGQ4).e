13
356482285569
789884796214014 1738769015564166200
{
  "name": "Megalodon",
  "local_enabled": true,
  "local_position": {
    "X": 36.3429641723632812,
    "Y": 33.5295715332031250
  },
  "local_rotation": 0,
  "local_scale": {
    "X": 3,
    "Y": 3
  },
  "previous_sibling": "197690942118438:1738446251876438800",
  "next_sibling": "790788144804297:1738769207764413300",
  "parent": "134953159748543:1738126268880005500",
  "spawn_as_networked_entity": true
},
{
  "cid": 1,
  "aoid": "789884796422788:1738769015564209800",
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
  "aoid": "789884796552743:1738769015564237400",
  "component_type": "Internal_Component",
  "internal_component_type": "Interactable",
  "data": {
    "text": "Fight Megalodon (Lv. 50)",
    "radius": 4
  }
},
{
  "cid": 2,
  "aoid": "789884796588463:1738769015564245100",
  "component_type": "Mono_Component",
  "mono_component_type": "Boss",
  "data": {
    "Interactable": "789884796552743:1738769015564237400",
    "SpineAnimator": "789884796422788:1738769015564209800",
    "FishId": "Megalodon",
    "Level": 50,
    "Type": "fire"
  }
}
