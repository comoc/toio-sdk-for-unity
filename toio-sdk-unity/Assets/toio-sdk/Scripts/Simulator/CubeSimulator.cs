﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace toio.Simulator
{
    public class CubeSimulator : MonoBehaviour
    {
        #pragma warning disable 0414
        #pragma warning disable 0649

        // ======== Physical Constants ========
        // from https://toio.github.io/toio-spec/
        public static readonly float TireWidthM = 0.0266f;
        public static readonly float TireWidthDot= 0.0266f * Mat.DotPerM;
        public static readonly float WidthM= 0.0318f;
        // ratio of Speed(Dot/s) and order ( 2.04f in real test )
        // theorically, 4.3 rpm/u * pi * 0.0125m / (60s/m) * DotPerM
        public static readonly float VMeterOverU = 4.3f*Mathf.PI*0.0125f/60;
        public static readonly float VDotOverU =  VMeterOverU * Mat.DotPerM; // about 2.06


        // ======== Simulator Settings ========
        public enum Version
        {
            v2_0_0,
            v2_1_0,
            v2_2_0
        }
        [SerializeField]
        public Version version = Version.v2_2_0;
        [SerializeField]
        public float motorTau = 0.04f; // parameter of one-order model for motor, τ
        [SerializeField]
        public float delay = 0.15f; // latency of communication

        [SerializeField]
        public bool forceStop = false;


        // ======== Properties ========

        /// <summary>
        /// モーター指令の最大値
        /// </summary>
        public int maxMotor { get{
            return impl.maxMotor;
        }}

        /// <summary>
        /// モーター指令のデッドゾーン
        /// </summary>
        public int deadzone { get{
            return impl.deadzone;
        }}

        /// <summary>
        /// シミュレータが初期化できたか
        /// </summary>
        public bool ready { get; private set; } = false;

        // ----- toio ID -----

        /// <summary>
        /// マット上の x 座標
        /// </summary>
        public int x { get {return impl.x;} internal set {impl.x = value;} }

        /// <summary>
        /// マット上の y 座標
        /// </summary>
        public int y { get {return impl.y;} internal set {impl.y = value;} }

        /// <summary>
        /// マット・スタンダードID上の角度
        /// </summary>
        public int deg { get {return impl.deg;} internal set {impl.deg = value;} }

        /// <summary>
        /// マット上の読み取りセンサーの y 座標
        /// </summary>
        public int xSensor { get {return impl.xSensor;} internal set {impl.xSensor = value;} }

        /// <summary>
        /// マット上の読み取りセンサーの y 座標
        /// </summary>
        public int ySensor { get {return impl.ySensor;} internal set {impl.ySensor = value;} }

        /// <summary>
        /// Standard ID の値
        /// </summary>
        public uint standardID { get {return impl.standardID;} internal set {impl.standardID = value;} }

        /// <summary>
        /// マット上にあるか
        /// </summary>
        public bool onMat { get {return impl.onMat;} internal set {impl.onMat = value;} }

        /// <summary>
        /// Standard ID 上にあるか
        /// </summary>
        public bool onStandardID { get {return impl.onStandardID;} internal set {impl.onStandardID = value;} }

        /// <summary>
        /// マット又はStandard ID 上にあるか
        /// </summary>
        public bool isGrounded { get {return onMat || onStandardID; } }

        // ----- Button -----

        /// <summary>
        /// ボタンが押されているか
        /// </summary>
        public bool button{ get {return impl.button;} internal set {impl.button = value;} }

        // ----- Motion Sensor -----
        // 2.0.0

        /// <summary>
        /// 水平検出をシミュレータがシミュレーションするか
        /// </summary>
        [HideInInspector]
        public bool isSimulateSloped = true;

        /// <summary>
        /// 傾斜であるか
        /// </summary>
        public bool sloped{ get {return impl.sloped;} internal set {impl.sloped = value;} }

        // 2.1.0

        /// <summary>
        /// ポーズ
        /// </summary>
        public Cube.PoseType pose{ get {return impl.pose;} internal set {impl.pose = value;} }

        // 2.2.0

        /// <summary>
        /// シェイクが検出されたか
        /// </summary>
        public int shakeLevel{ get {return impl.shakeLevel;} internal set {impl.shakeLevel = value;} }

        /// <summary>
        /// コアキューブのモーター ID 1（左）の速度
        /// </summary>
        public int leftMotorSpeed{ get {return impl.leftMotorSpeed;} }

        /// <summary>
        /// コアキューブのモーター ID 2（右）の速度
        /// </summary>
        public int rightMotorSpeed{ get {return impl.rightMotorSpeed;} }


        // ======== Objects ========
        private Rigidbody rb;
        private AudioSource audioSource;
        private GameObject cubeModel;
        private GameObject LED;
        private BoxCollider col;

        private CubeSimImpl impl;



        private void Start()
        {
            #if !(UNITY_EDITOR || UNITY_STANDALONE)   // Editor以外で実行される場合は自身を無効かします
                this.gameObject.SetActive(false);
            #else
                this.rb = GetComponent<Rigidbody>();
                this.rb.maxAngularVelocity = 21f;
                this.audioSource = GetComponent<AudioSource>();
                this.LED = transform.Find("LED").gameObject;
                this.LED.GetComponent<Renderer>().material.color = Color.black;
                this.cubeModel = transform.Find("cube_model").gameObject;
                this.col = GetComponent<BoxCollider>();

                switch (version)
                {
                    case Version.v2_0_0 : this.impl = new CubeSimImpl_v2_0_0(this);break;
                    case Version.v2_1_0 : this.impl = new CubeSimImpl_v2_1_0(this);break;
                    case Version.v2_2_0 : this.impl = new CubeSimImpl_v2_2_0(this);break;
                    default : this.impl = new CubeSimImpl_v2_2_0(this);break;
                }
                this._InitPresetSounds();
            #endif
        }

        private void Update()
        {
        }

        private void FixedUpdate()
        {
            SimulatePhysics();

            impl.Simulate();

            this.ready = true;  // 一回更新してからシミュレーターがreadyになる
        }

        internal bool offGroundL = true;
        internal bool offGroundR = true;
        private void SimulatePhysics()
        {
            // タイヤの着地状態を調査
            // Check if tires are Off Ground
            RaycastHit hit;
            var ray = new Ray(transform.position+transform.up*0.001f-transform.right*0.0133f, -transform.up); // left wheel
            if (Physics.Raycast(ray, out hit) && hit.distance < 0.002f) offGroundL = false;
            ray = new Ray(transform.position+transform.up*0.001f+transform.right*0.0133f, -transform.up); // right wheel
            if (Physics.Raycast(ray, out hit) && hit.distance < 0.002f) offGroundR = false;
        }



        // ============ Event ============

        // ------------ v2.0.0 ------------

        /// <summary>
        /// ボタンのイベントコールバックを設定する
        /// </summary>
        public void StartNotification_Button(System.Action<bool> action)
        {
            impl.StartNotification_Button(action);
        }

        /// <summary>
        /// Standard ID のイベントコールバックを設定する
        /// </summary>
        public void StartNotification_StandardID(System.Action<uint, int> action)
        {
            impl.StartNotification_StandardID(action);
        }

        /// <summary>
        /// Standard ID から持ち上げられた時のイベントコールバックを設定する
        /// </summary>
        public void StartNotification_StandardIDMissed(System.Action action)
        {
            impl.StartNotification_StandardIDMissed(action);
        }

        /// <summary>
        /// Position ID （マット座標）のイベントコールバックを設定する
        /// </summary>
        public void StartNotification_PositionID(System.Action<int, int, int, int, int> action)
        {
            impl.StartNotification_PositionID(action);
        }

        /// <summary>
        /// マットから持ち上げられた時のイベントコールバックを設定する
        /// </summary>
        public void StartNotification_PositionIDMissed(System.Action action)
        {
            impl.StartNotification_PositionIDMissed(action);
        }

        /// <summary>
        /// 水平検出のイベントコールバックを設定する
        /// </summary>
        public void StartNotification_MotionSensor(System.Action<object[]> action)
        {
            impl.StartNotification_MotionSensor(action);
        }


        /// <summary>
        /// 目標指定付きモーター制御の応答コールバックを設定する
        /// </summary>
        public void StartNotification_TargetMove(System.Action<int, Cube.TargetMoveRespondType> action)
        {
            impl.StartNotification_TargetMove(action);
        }

        /// <summary>
        /// 複数目標指定付きモーター制御の応答コールバックを設定する
        /// </summary>
        public void StartNotification_MultiTargetMove(System.Action<int, Cube.TargetMoveRespondType> action)
        {
            impl.StartNotification_MultiTargetMove(action);
        }

        /// <summary>
        /// モーター速度読み取りのイベントコールバックを設定する
        /// </summary>
        public void StartNotification_MotorSpeed(System.Action<int, int> action)
        {
            impl.StartNotification_MotorSpeed(action);
        }

        /// <summary>
        /// 設定の応答の読み出しコールバックを設定する
        /// 引数：モーター速度設定応答
        /// </summary>
        public void StartNotification_ConfigMotorRead(System.Action<bool> action)
        {
            impl.StartNotification_ConfigMotorRead(action);
        }


        // ============ コマンド ============

        // --------- 2.0.0 --------
        /// <summary>
        /// モーター：時間指定付きモーター制御
        /// </summary>
        public void Move(int left, int right, int durationMS)
        {
            impl.Move(left, right, durationMS);
        }

        /// <summary>
        /// ランプ：消灯
        /// </summary>
        public void StopLight()
        {
            impl.StopLight();
        }
        /// <summary>
        /// ランプ：点灯
        /// </summary>
        public void SetLight(int r, int g, int b, int durationMS)
        {
            impl.SetLight(r, g, b, durationMS);
        }
        /// <summary>
        /// ランプ：連続的な点灯・消灯
        /// </summary>
        public void SetLights(int repeatCount, Cube.LightOperation[] operations)
        {
            impl.SetLights(repeatCount, operations);
        }

        /// <summary>
        /// サウンド：MIDI note number の再生
        /// </summary>
        public void PlaySound(int repeatCount, Cube.SoundOperation[] operations)
        {
            impl.PlaySound(repeatCount, operations);
        }
        /// <summary>
        /// サウンド：効果音の再生
        /// </summary>
        public void PlayPresetSound(int soundId, int volume)
        {
            impl.PlayPresetSound(soundId, volume);
        }
        /// <summary>
        /// サウンド：再生の停止
        /// </summary>
        public void StopSound()
        {
            impl.StopSound();
        }

        /// <summary>
        /// 水平検出の閾値を設定する（度）
        /// </summary>
        public void ConfigSlopeThreshold(int degree)
        {
            impl.ConfigSlopeThreshold(degree);
        }

        // --------- 2.1.0 --------
        public void TargetMove(
            int targetX,
            int targetY,
            int targetAngle,
            int configID,
            int timeOut,
            Cube.TargetMoveType targetMoveType,
            int maxSpd,
            Cube.TargetSpeedType targetSpeedType,
            Cube.TargetRotationType targetRotationType
        ){
            impl.TargetMove(targetX, targetY, targetAngle, configID, timeOut, targetMoveType, maxSpd, targetSpeedType, targetRotationType);
        }

        public void MultiTargetMove(
            int[] targetXList,
            int[] targetYList,
            int[] targetAngleList,
            Cube.TargetRotationType[] multiRotationTypeList,
            int configID,
            int timeOut,
            Cube.TargetMoveType targetMoveType,
            int maxSpd,
            Cube.TargetSpeedType targetSpeedType,
            Cube.MultiWriteType multiWriteType
        ){
            impl.MultiTargetMove(targetXList, targetYList, targetAngleList, multiRotationTypeList, configID, timeOut, targetMoveType, maxSpd, targetSpeedType, multiWriteType);
        }

        public void AccelerationMove(
            int targetSpeed,
            int acceleration,
            int rotationSpeed,
            Cube.AccPriorityType accPriorityType,
            int controlTime
        ){
            impl.AccelerationMove(targetSpeed, acceleration, rotationSpeed, accPriorityType, controlTime);
        }

        // --------- 2.2.0 --------
        /// <summary>
        /// モーターの速度情報の取得の設定
        /// </summary>
        public void ConfigMotorRead(bool enabled)
        {
            impl.ConfigMotorRead(enabled);
        }

        public void RequestSensor()
        {
            impl.RequestSensor();
        }


        // ====== 内部関数 ======

        // Sensor Triggers

        internal void _TriggerCollision()
        {
            this.impl.TriggerCollision();
        }

        internal void _TriggerDoubleTap()
        {
            this.impl.TriggerDoubleTap();
        }

        // 速度変化によって力を与え、位置と角度を更新
        internal void _SetSpeed(float speedL, float speedR)
        {
            this.rb.angularVelocity = transform.up * (float)((speedL - speedR) / TireWidthM);
            var vel = transform.forward * (speedL + speedR) / 2;
            var dv = vel - this.rb.velocity;
            this.rb.AddForce(dv, ForceMode.VelocityChange);
        }
        internal void _SetLight(int r, int g, int b){
            r = Mathf.Clamp(r, 0, 255);
            g = Mathf.Clamp(g, 0, 255);
            b = Mathf.Clamp(b, 0, 255);
            LED.GetComponent<Renderer>().material.color = new Color(r/255f, g/255f, b/255f);
        }

        internal void _StopLight(){
            LED.GetComponent<Renderer>().material.color = Color.black;
        }

        private int playingSoundId = -1;
        internal void _PlaySound(int soundId, int volume){
            if (soundId >= 128) { _StopSound(); playingSoundId = -1; return; }
            if (soundId != playingSoundId)
            {
                playingSoundId = soundId;
                int octave = (int)(soundId/12);
                int idx = (int)(soundId%12);
                var aCubeOnSlot = Resources.Load("Octave/" + (octave*12+9)) as AudioClip;
                audioSource.pitch = (float)Math.Pow(2, ((float)idx-9)/12);
                audioSource.clip = aCubeOnSlot;
            }
            audioSource.volume = (float)volume/256;
            if (!audioSource.isPlaying)
                audioSource.Play();
        }
        internal void _StopSound(){
            audioSource.clip = null;
            audioSource.Stop();
        }

        // Sound Preset を設定
        internal void _InitPresetSounds(){
            impl.presetSounds.Add( new Cube.SoundOperation[2]
            {
                new Cube.SoundOperation(60, 255, 71),
                new Cube.SoundOperation(60, 255, 67),
            });
            impl.presetSounds.Add( new Cube.SoundOperation[2]
            {
                new Cube.SoundOperation(40, 255, 78),
                new Cube.SoundOperation(200, 255, 81),
            });
            impl.presetSounds.Add( new Cube.SoundOperation[3]
            {
                new Cube.SoundOperation(70, 255, 69),
                new Cube.SoundOperation(60, 255, 67),
                new Cube.SoundOperation(60, 255, 66),
            });
            impl.presetSounds.Add( new Cube.SoundOperation[1]
            {
                new Cube.SoundOperation(70, 255, 69),
            });
            impl.presetSounds.Add( new Cube.SoundOperation[4]
            {
                new Cube.SoundOperation(120, 255, 62),
                new Cube.SoundOperation(120, 255, 67),
                new Cube.SoundOperation(150, 255, 71),
                new Cube.SoundOperation(240, 255, 67),
            });
            impl.presetSounds.Add( new Cube.SoundOperation[4]
            {
                new Cube.SoundOperation(120, 255, 69),
                new Cube.SoundOperation(150, 255, 71),
                new Cube.SoundOperation(150, 255, 67),
                new Cube.SoundOperation(240, 255, 62),
            });
            impl.presetSounds.Add( new Cube.SoundOperation[4]
            {
                new Cube.SoundOperation(60, 255, 66),
                new Cube.SoundOperation(80, 255, 69),
                new Cube.SoundOperation(40, 255, 74),
                new Cube.SoundOperation(60, 255, 76),
            });
            impl.presetSounds.Add( new Cube.SoundOperation[5]
            {
                new Cube.SoundOperation(80, 255, 74),
                new Cube.SoundOperation(30, 255, 128),
                new Cube.SoundOperation(80, 255, 74),
                new Cube.SoundOperation(30, 255, 128),
                new Cube.SoundOperation(140, 255, 81),
            });
            impl.presetSounds.Add( new Cube.SoundOperation[3]
            {
                new Cube.SoundOperation(60, 255, 71),
                new Cube.SoundOperation(60, 255, 67),
                new Cube.SoundOperation(120, 255, 74),
            });
            impl.presetSounds.Add( new Cube.SoundOperation[1]
            {
                new Cube.SoundOperation(70, 255, 74),
            });
            impl.presetSounds.Add( new Cube.SoundOperation[1]
            {
                new Cube.SoundOperation(70, 255, 66),
            });
        }

        internal void _SetPressed(bool pressed)
        {
            this.cubeModel.transform.localEulerAngles
                    = pressed? new Vector3(-93,0,0) : new Vector3(-90,0,0);
        }



    }

}