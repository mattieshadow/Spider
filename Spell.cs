using UnityEngine;
using ThunderRoad;
using UnityEngine.UI;
using Slider = UnityEngine.UI.Slider;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
namespace Spider
{
    public class Spell : SpellCastCharge
    {
        // Script related stuff.
        public static Spell leftInstance, rightInstance;
        public static float webPower = 2, tetherPower = 60, tetherTime = 120f;
        public float maxRange;
        static bool bindedEvent;
        public bool acting;
        GameObject activeWebHolder, reticleHolder;
        Item webbedItem;
        Creature webbedCreature;
        public Vector3 offset, webbedPoint;
        Rigidbody attachedRB;
        LineRenderer activeWeb, reticle;
        float webDistance;
        public static Dictionary<Tether, float> tetherToTime = new Dictionary<Tether, float>();
        public static Dictionary<Web, float> webToTime = new Dictionary<Web, float>();
        public static Spell latestInstance;

        // JSON related stuff.
        public Color webColor;
        public bool visibleStringAim;
        public float webSize;

        public override void Load(SpellCaster spellCaster, Level level)
        {
            base.Load(spellCaster, level);
            latestInstance = this;
            activeWebHolder = new GameObject();
            activeWebHolder.transform.position = spellCaster.ragdollHand.transform.position;
            activeWebHolder.transform.parent = spellCaster.transform;
            activeWeb = activeWebHolder.AddComponent<LineRenderer>();
            activeWeb.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            activeWeb.material.SetColor("_BaseColor", webColor);
            activeWeb.widthMultiplier = webSize * 0.003f;
            activeWeb.enabled = false;
            if (visibleStringAim)
            {
                reticleHolder = new GameObject();
                reticle = reticleHolder.AddComponent<LineRenderer>();
                Catalog.LoadAssetAsync<Material>("Spider.ReticleMaterial", material =>
                {
                    reticle.material = material;
                }, "Spider");
                reticle.widthMultiplier = 0.0015f;
                reticle.generateLightingData = false;
                reticle.lightProbeUsage = LightProbeUsage.Off;
                reticle.receiveShadows = false;
                reticle.enabled = true;
            }
            if (spellCaster.ragdollHand.side == Side.Left) leftInstance = this;
            if (spellCaster.ragdollHand.side == Side.Right) rightInstance = this;
            spellCaster.ragdollHand.gameObject.AddComponent<HandTetherAbility>();
            if (!bindedEvent)
            {
                bindedEvent = true;
                EventManager.onCreatureSpawn += Spawn;
                EventManager.onLevelUnload += UnLoad;
                Keyframe[] frames = Player.currentCreature.data.playerFallDamageCurve.keys;
                for (int i = 0; i < frames.Length; i++)
                {
                    Keyframe frame = frames[i];
                    frame.time *= 50;
                    frame.value /= 50;
                }
                Player.currentCreature.data.playerFallDamageCurve.keys = frames;
                Player.currentCreature.data.playerFallDamageCurve.preWrapMode = WrapMode.Once;
                Player.currentCreature.data.playerFallDamageCurve.postWrapMode = WrapMode.PingPong;
            }
        }
        public static void UnLoad(LevelData levelData, EventTime eventTime)
        {
            ClearTethers();
        }
        public static void ClearTethers()
        {
            HashSet<Tether> temp = tetherToTime.Keys.ToHashSet();
            foreach (Tether tether in temp) tether.Destroy();
            tetherToTime.Clear();
        }
        public static void Spawn(Creature creature)
        {
            if (creature == null) return;
            if (leftInstance == null && rightInstance == null) return;
            if (leftInstance != null && leftInstance.attachedRB && leftInstance.attachedRB.GetComponentInParent<Creature>() is Creature webbedCreature && webbedCreature == creature)
            {
                leftInstance.CancelWeb();
            }
            if (rightInstance != null && rightInstance.attachedRB && rightInstance.attachedRB.GetComponentInParent<Creature>() is Creature webbedCreature2 && webbedCreature2 == creature)
            {
                rightInstance.CancelWeb();
            }
            HashSet<Tether> tempHash = tetherToTime.Keys.ToHashSet();
            foreach (Tether tether in tempHash) // Tether stuff!
            {
                if (tether.rb.gameObject.GetComponentInParent<Creature>() is Creature creature1 && creature1 == creature)
                {
                    tether.Destroy();
                    tetherToTime.Remove(tether);
                }
                if (tether.rb2 && tether.rb2.gameObject.GetComponentInParent<Creature>() is Creature creature2 &&  creature2 == creature)
                {
                    tether.Destroy();
                    tetherToTime.Remove(tether);
                }
            }
        }
        public override void Fire(bool active)
        {
            base.Fire(active);
            if (active)
            {
                Fire(false);               //
                currentCharge = 0;              // Disable the spell (visibly)
                spellCaster.isFiring = false;   //
                acting = true;                  // Telling active to enable, letting the script know to cast webs
            }

        }
        public override void UpdateCaster()
        {
            base.UpdateCaster();
            if (visibleStringAim)
            {
                reticle.SetPositions(new Vector3[]
                {
                    spellCaster.ragdollHand.transform.position,
                    spellCaster.ragdollHand.transform.position + -(spellCaster.ragdollHand.transform.right * 75)
                });
            }
            if (acting) // Actual web stuff
            {
                if (PlayerControl.GetHand(spellCaster.ragdollHand.side)
                    .castPressed) // Player is pressing in cast/trigger on the spellCaster's hand
                {
                    if (!attachedRB && webbedPoint == Vector3.zero)
                        if (Physics.Raycast(
                            spellCaster.ragdollHand.transform.position +
                            (spellCaster.ragdollHand.transform.right * -0.175f),
                            -spellCaster.ragdollHand.transform.right, out RaycastHit hit, maxRange))
                        {
                            if (hit.rigidbody && !hit.rigidbody.isKinematic)
                            {
                                attachedRB = hit.rigidbody;
                                offset = attachedRB.transform.InverseTransformPoint(hit.point);
                                if (attachedRB.GetComponentInParent<Creature>() is Creature creature)
                                {
                                    webbedCreature = creature;
                                    if (!attachedRB.GetComponent<RagdollPart>())
                                    {
                                        RagdollPart closestPart = creature.ragdoll.parts.OrderBy(i => Vector3.Distance(i.transform.position, hit.point)).First();
                                        attachedRB = closestPart.rb; // If it hit a creature but not a part, it will make sure to get the nearest part to that hit instead of just the creature's locomotion collider
                                        offset = attachedRB.transform.InverseTransformPoint(closestPart.transform.position);
                                    }
                                }
                                if (attachedRB.GetComponentInParent<Item>() is Item item && item.data.purchasable) webbedItem = item;
                            }
                            else webbedPoint = hit.point;
                            webDistance = hit.distance - 0.25f;
                            activeWeb.enabled = true;
                            if (reticle) reticle.enabled = false; // Tell the reticle to disable if it exists. We are now shooting a web so why should it be visible?
                        }

                    if (attachedRB || webbedPoint != Vector3.zero) // If web has a valid attach point.
                    {
                        Vector3 point = attachedRB ? attachedRB.transform.TransformPoint(offset) : webbedPoint;
                        float distance = Vector3.Distance(point, spellCaster.ragdollHand.transform.position);
                        Locomotion correctLocomotion = spellCaster.ragdollHand.creature == Player.currentCreature ? Player.local.locomotion : spellCaster.ragdollHand.creature.locomotion;
                        Vector3 towardsPoint = ((attachedRB ? spellCaster.ragdollHand.transform.position : webbedPoint) - (attachedRB ? attachedRB.transform.position : spellCaster.ragdollHand.creature.transform.position)).normalized;
                        activeWeb.enabled = true;
                        activeWeb.SetPositions(new Vector3[]
                        {
                            point, // Attach to the attachedRB if it exists. If it doesn't, instead, attach to the webbedPoint.
                            spellCaster.ragdollHand.transform.position
                        });
                        if (distance > webDistance && !PlayerControl.GetHand(spellCaster.ragdollHand.side).gripPressed) // If the attached point and or object is FURTHER than (webDistance).
                        {
                            (attachedRB ? attachedRB : correctLocomotion.rb).AddForce(towardsPoint * (webPower * Mathf.Min(distance * 1.25f, 30) * (webbedCreature ? Mathf.Abs(webbedCreature.ragdoll.parts.Where(part => !part.isSliced).Count() / webbedCreature.ragdoll.parts.Count - 1) + 2: 1.25f) * (attachedRB == null ? 0.75f : 1)), attachedRB ? ForceMode.Impulse : ForceMode.Acceleration); // Deciphers whether or not the webbed object exists, if it does it will pull it towards the caster, if it's not, it will pull the caster to the webbedPoint (MULTIPLIED BY DISTANCE)
                        }
                        if (PlayerControl.GetHand(spellCaster.ragdollHand.side).gripPressed) // If the attached point and or object is FURTHER than (webDistance).
                        {
                            (attachedRB ? attachedRB : correctLocomotion.rb).AddForce(towardsPoint * (webPower * Mathf.Min(distance * 1.35f, 40) * (webbedCreature ? Mathf.Abs(webbedCreature.ragdoll.parts.Where(part => !part.isSliced).Count() / webbedCreature.ragdoll.parts.Count - 1) + 2 : 1.25f) * (attachedRB == null ? 0.85f : 1)), attachedRB ? ForceMode.Impulse : ForceMode.Acceleration); // Deciphers whether or not the webbed object exists, if it does it will pull it towards the caster, if it's not, it will pull the caster to the webbedPoint (MULTIPLIED BY DISTANCE)
                        }
                        if (distance + 0.25f < webDistance) // If the distance of webbedPoint/Obj & hand is CLOSER than the web distance.
                        {
                            webDistance = distance - 0.25f; // Changes the distance to the current, assuming it's closer than the distance. 
                        }
                        if (Vector3.Dot(correctLocomotion.velocity.normalized, Vector3.down) > 0.8f && webbedPoint != Vector3.zero && spellCaster.ragdollHand.transform.position.y < point.y - (webDistance * 0.9f))
                        {
                            correctLocomotion.rb.AddForce(Vector3.up * webPower, ForceMode.Acceleration); // Apply upwards force if they're falling straight down to try to counteract sometimes when the player is trying to elevate but just isn't strong enough to relieve their fall
                        }
                        if (webbedCreature && attachedRB && Vector3.Distance(point, spellCaster.ragdollHand.transform.position) > 20 / (webPower * 1.5f) && !webbedCreature.isKilled && webbedCreature.ragdoll.state != Ragdoll.State.Destabilized)
                        {
                            webbedCreature.ragdoll.SetState(Ragdoll.State.Destabilized); // Destabilize the creature if your web has enough strength and  you're webbing them
                        }
                        if (attachedRB) correctLocomotion.rb.AddForce((attachedRB.transform.position - correctLocomotion.transform.position).normalized * Mathf.Min(distance, 20) * (webbedCreature ? webbedCreature.ragdoll.parts.Where(part => !part.isSliced).Count() / 3 : 2) * (PlayerControl.GetHand(spellCaster.ragdollHand.side).gripPressed ? 2 : 0.5f), ForceMode.Acceleration); // Receive some force back while webbing on an rigidbody
                        if (webbedItem && (webbedItem.transform.position - spellCaster.ragdollHand.transform.position).sqrMagnitude < 1.2f * 1.2f) // Check if you have a webbed item, and if you do check if it's close enough to the hand. If it is, grab it and cancel the web!
                        {
                            spellCaster.ragdollHand.Grab(webbedItem.GetMainHandle(spellCaster.ragdollHand.side));
                            CancelWeb();
                        }
                    }
                    else activeWeb.enabled = false;
                }
                else
                {
                    CancelWeb();
                }
            }
            HashSet<Tether> tempHash = tetherToTime.Keys.ToHashSet();
            foreach (Tether tether in tempHash) // Tether stuff!
            {
                if (tetherToTime[tether] <= 0)
                {
                    tether.Destroy();
                    tetherToTime.Remove(tether);
                    continue;
                }
                tether.Pull();
                tetherToTime[tether] -= Time.deltaTime;
            }
            HashSet<Web> tempHash2 = webToTime.Keys.ToHashSet();
            foreach (Web web in tempHash2) // Tether stuff!
            {
                if (webToTime[web] <= 0)
                {
                    web.Destroy();
                    webToTime.Remove(web);
                    continue;
                }
                webToTime[web] -= Time.deltaTime;
            }
        }
        public void CancelWeb()
        {
            acting = false;
            activeWeb.enabled = false;
            attachedRB = null;
            webbedPoint = Vector3.zero;
            if (reticle) reticle.enabled = true; // Tell the reticle to enable if it exists. We stopped shooting a web so it should be visible for aiming purposes again.
            webbedItem = null;
            webbedCreature = null;
        }

        public override void Unload()
        {
            base.Unload();
            HashSet<Tether> temp = tetherToTime.Keys.ToHashSet();
            foreach (Tether tether in temp) tether.Destroy();
            tetherToTime.Clear();
            Object.Destroy(spellCaster.ragdollHand.gameObject.GetComponent<HandTetherAbility>());
            Object.Destroy(activeWebHolder);
            if (reticleHolder) Object.Destroy(reticleHolder);
        }
        public static void AttemptTether()
        {
            if (leftInstance == null || rightInstance == null) return;
            if (leftInstance.webbedPoint != Vector3.zero && rightInstance.webbedPoint != Vector3.zero) webToTime.Add(new Web(leftInstance.webbedPoint, rightInstance.webbedPoint), tetherTime);
            if (leftInstance.attachedRB && rightInstance.attachedRB) tetherToTime.Add(new Tether(leftInstance.attachedRB, rightInstance.attachedRB, Vector3.zero, leftInstance.offset, rightInstance.offset), tetherTime);
            if (rightInstance.attachedRB && !leftInstance.attachedRB && leftInstance.webbedPoint != Vector3.zero) tetherToTime.Add(new Tether(rightInstance.attachedRB, null, leftInstance.webbedPoint, rightInstance.offset, Vector3.zero), tetherTime);
            if (!rightInstance.attachedRB && leftInstance.attachedRB && rightInstance.webbedPoint != Vector3.zero) tetherToTime.Add(new Tether(leftInstance.attachedRB, null, rightInstance.webbedPoint, leftInstance.offset, Vector3.zero), tetherTime);
            leftInstance.CancelWeb();
            rightInstance.CancelWeb();
        }

        public class Web
        {
            public GameObject gameObject;
            public LineRenderer lineRenderer;
            public Vector3 firstPoint, secondPoint;
            public Web(Vector3 point1, Vector3 point2)
            {
                lineRenderer = (gameObject = GameObject.CreatePrimitive(PrimitiveType.Capsule)).AddComponent<LineRenderer>();
                firstPoint = point1;
                secondPoint = point2;
                gameObject.transform.position = Vector3.Lerp(point1, point2, 0.5f);
                gameObject.transform.up = (point1 - point2).normalized;
                gameObject.transform.localScale = new Vector3(0.05f, (point1 - point2).magnitude / 2, 0.05f);
                NavMeshObstacle obstacle = gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Capsule;
                obstacle.center = Vector3.zero;
                obstacle.size = Vector3.one;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                lineRenderer.material.SetColor("_BaseColor", latestInstance.webColor);
                lineRenderer.widthMultiplier = 0.0075f;
                Object.Destroy(gameObject.GetComponent<MeshRenderer>());
                lineRenderer.SetPositions(new Vector3[]
                {
                    point1,
                    point2
                });
            }
            public void Destroy()
            {
                Object.Destroy(gameObject);
            }
        }

        public class Tether
        {
            public Rigidbody rb, rb2;
            public Vector3 point, offset1, offset2;
            public Creature possibleCreature, secondPossibleCreature;
            bool rb2Exists, firstCreatureExists, secondCreatureExists, shouldTrip, shouldDisarm;
            GameObject holder;
            LineRenderer web;
            public Tether(Rigidbody rb, Rigidbody rb2, Vector3 point, Vector3 offset1, Vector3 offset2)
            {
                this.rb = rb;
                this.rb2 = rb2;
                if (this.rb2 != null) rb2Exists = true;
                this.point = point;
                this.offset1 = offset1;
                this.offset2 = offset2;
                possibleCreature = rb.GetComponentInParent<Creature>();
                if (rb2 != null) secondPossibleCreature = rb2.GetComponentInParent<Creature>();
                if (possibleCreature) firstCreatureExists = true;
                if (secondCreatureExists) secondCreatureExists = true;
                holder = new GameObject();
                web = holder.AddComponent<LineRenderer>();
                web.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                web.material.SetColor("_BaseColor", rightInstance.webColor);
                web.widthMultiplier = 0.0032f * leftInstance.webSize;
                web.enabled = true;
                if (possibleCreature != null && secondPossibleCreature != null && possibleCreature == secondPossibleCreature)
                {
                    RagdollPart.Type type1 = RagdollPart.Type.Torso, type2 = RagdollPart.Type.Torso;
                    foreach (Rigidbody rigidbody in possibleCreature.ragdoll.parts.Select(part => part.rb))
                    {
                        if (rigidbody == rb) type1 = rigidbody.GetComponent<RagdollPart>().type;
                    } 
                    foreach (Rigidbody rigidbody2 in possibleCreature.ragdoll.parts.Select(part => part.rb))
                    {
                        if (rigidbody2 == rb2) type2 = rigidbody2.GetComponent<RagdollPart>().type;
                    }
                    if ((type1 == RagdollPart.Type.LeftLeg || type1 == RagdollPart.Type.RightLeg || type1 == RagdollPart.Type.RightFoot || type1 == RagdollPart.Type.LeftFoot) && (type2 == RagdollPart.Type.LeftLeg || type2 == RagdollPart.Type.RightLeg || type2 == RagdollPart.Type.RightFoot || type2 == RagdollPart.Type.LeftFoot))
                    {
                        shouldTrip = true;
                    }
                    if ((type1 == RagdollPart.Type.LeftArm || type1 == RagdollPart.Type.LeftHand || type1 == RagdollPart.Type.RightArm || type1 == RagdollPart.Type.RightHand) && (type2 == RagdollPart.Type.LeftArm || type2 == RagdollPart.Type.LeftHand || type2 == RagdollPart.Type.RightArm || type2 == RagdollPart.Type.RightHand))
                    {
                        shouldDisarm = true;
                    }
                    if ((type1 == RagdollPart.Type.LeftArm || type1 == RagdollPart.Type.LeftHand || type1 == RagdollPart.Type.RightArm || type1 == RagdollPart.Type.RightHand) && (type2 == RagdollPart.Type.LeftLeg || type2 == RagdollPart.Type.RightLeg || type2 == RagdollPart.Type.RightFoot || type2 == RagdollPart.Type.LeftFoot))
                    {
                        shouldDisarm = true;
                        shouldTrip = true;
                    }
                    if ((type1 == RagdollPart.Type.LeftLeg || type1 == RagdollPart.Type.RightLeg || type1 == RagdollPart.Type.RightFoot || type1 == RagdollPart.Type.LeftFoot) && (type2 == RagdollPart.Type.LeftArm || type2 == RagdollPart.Type.LeftHand || type2 == RagdollPart.Type.RightArm || type2 == RagdollPart.Type.RightHand))
                    {
                        shouldDisarm = true;
                        shouldTrip = true;
                    }
                } 
            }
            public void Destroy()
            {
                Object.Destroy(holder);
            }
            public void Pull()
            {
                if (rb == null) return;
                if (rb2Exists && rb2 == null) return;
                if (!possibleCreature && firstCreatureExists) return;
                if (!secondPossibleCreature && secondCreatureExists) return;
                try
                {
                    web.SetPositions(new Vector3[]
                    {
                        rb.transform.TransformPoint(offset1),
                        rb2 != null ? rb2.transform.TransformPoint(offset2) : point
                    });
                }
                catch
                {

                }
                float distance = Vector3.Distance(rb.position, rb2 != null ? rb2.position : point);
                if (rb && rb2 != null&& point == Vector3.zero)
                {
                    rb.AddForce((rb2.transform.position - rb.transform.position).normalized * tetherPower * tetherPower * Mathf.Min(distance, 10) * Time.timeScale);
                    rb2.AddForce((rb.transform.position - rb2.transform.position).normalized * tetherPower * tetherPower * Mathf.Min(distance, 10) * Time.timeScale);
                }
                if (rb && point != Vector3.zero && rb2 == null)
                {
                    rb.AddForce((point - rb.transform.position).normalized * tetherPower * tetherPower * Mathf.Min(distance, 10) * Time.timeScale);
                }
                if (possibleCreature && !possibleCreature.isKilled && point != Vector3.zero) possibleCreature.ragdoll.SetState(Ragdoll.State.Destabilized);
                if (secondPossibleCreature && !secondPossibleCreature.isKilled && point != Vector3.zero) secondPossibleCreature.ragdoll.SetState(Ragdoll.State.Destabilized);
                if (shouldTrip && !possibleCreature.isKilled) possibleCreature.ragdoll.SetState(Ragdoll.State.Destabilized);
                if (shouldDisarm && possibleCreature.handRight.grabbedHandle) possibleCreature.handRight.UnGrab(false);
                if (shouldDisarm && possibleCreature.handLeft.grabbedHandle) possibleCreature.handLeft.UnGrab(false);
            }
        }
        class HandTetherAbility : MonoBehaviour
        {
            void OnCollisionEnter(Collision collision)
            {
                if (collision.collider.transform.name == "Palm") AttemptTether();
            }
        }
    }
    public class SpiderMenu : MenuModule
    {
        Button climbToggle;
        Button clearTether;
        Slider powerSlider;
        Slider tetherSlider;
        Slider timeSlider;
        Text powerText;
        Text tetherText;
        Text timeText;
        public override void Init(MenuData menuData, Menu menu)
        {
            base.Init(menuData, menu);
            climbToggle = menu.GetCustomReference("Climb").GetComponent<Button>();
            clearTether = menu.GetCustomReference("ClearTether").GetComponent<Button>();
            powerSlider = menu.GetCustomReference("Power").GetComponent<Slider>();
            tetherSlider = menu.GetCustomReference("Tether").GetComponent<Slider>();
            timeSlider = menu.GetCustomReference("Time").GetComponent<Slider>();
            powerText = menu.GetCustomReference("PowerText").GetComponent<Text>();
            timeText = menu.GetCustomReference("TimeText").GetComponent<Text>();
            tetherText = menu.GetCustomReference("TetherText").GetComponent<Text>();
            powerSlider.onValueChanged.AddListener(PowerSlider);
            tetherSlider.onValueChanged.AddListener(TetherSlider);
            timeSlider.onValueChanged.AddListener(TimeSlider);
            climbToggle.onClick.AddListener(ToggleClimb);
            clearTether.onClick.AddListener(Wipe);
            powerSlider.value = Spell.webPower;
            tetherSlider.value = Spell.tetherPower;
            timeSlider.value = Spell.tetherTime;
        }
        void Wipe()
        {
            HashSet<Spell.Tether> temp = Spell.tetherToTime.Keys.ToHashSet();
            foreach (Spell.Tether tether in temp) tether.Destroy();
            Spell.tetherToTime.Clear();
        }
        void ToggleClimb()
        {
            if (RagdollHandClimb.climbFree)
            {
                RagdollHandClimb.climbFree = false;
                climbToggle.GetComponentInChildren<Text>().text = "Climb is off";
            }
            else
            {
                RagdollHandClimb.climbFree = true;
                climbToggle.GetComponentInChildren<Text>().text = "Climb is on";
            }
        }
        void PowerSlider(float value)
        {
            Spell.webPower = value;
            powerText.text = "WebPower: " + value;
        }
        void TetherSlider(float value)
        {
            Spell.tetherPower = value;
            tetherText.text = "TetherPower: " + value;
        }
        void TimeSlider(float value)
        {
            Spell.tetherTime = value;
            timeText.text = "TetherTime: " + value;
        }
    }
}