13
382252089345
1277009504769564 1738872658770529400
{
  "name": "Fight Diver (30)",
  "local_enabled": true,
  "local_position": {
    "X": -45.9431571960449219,
    "Y": 0.4694578647613525
  },
  "local_rotation": 0,
  "local_scale": {
    "X": 1,
    "Y": 1
  },
  "previous_sibling": "1276787096962051:1738872611449816600",
  "next_sibling": "89279924232145:1739551681927199400",
  "parent": "134953159748543:1738126268880005500",
  "spawn_as_networked_entity": true
},
{
  "cid": 1,
  "aoid": "1277009504979654:1738872658770573700",
  "component_type": "Internal_Component",
  "internal_component_type": "Spine_Animator",
  "data": {
    "skeleton_data_asset": "Animations/player/playercharacter.spine",
    "ordered_skins": [
      "face/scuba_mask",
      "body/scuba_suit",
      "base/orange1"
    ]
  }
},
{
  "cid": 3,
  "aoid": "1277009505117176:1738872658770602900",
  "component_type": "Internal_Component",
  "internal_component_type": "Interactable",
  "data": {
    "text": "Fight Diver (Lv. 30)"
  }
},
{
  "cid": 2,
  "aoid": "1277009505157220:1738872658770611500",
  "component_type": "Mono_Component",
  "mono_component_type": "FightNPC",
  "data": {
    "Interactable": "1277009505117176:1738872658770602900",
    "SpineAnimator": "1277009504979654:1738872658770573700",
    "battleType": 1,
    "medianTeamLevel": 30
  }
}
