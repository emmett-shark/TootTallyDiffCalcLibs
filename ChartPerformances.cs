﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace TootTallyDiffCalcLibs
{
    public struct ChartPerformances : IDisposable
    {
        public static readonly float[] weights = {
             1.0000f, 0.8500f, 0.7225f, 0.6141f, 0.5220f, 0.4437f, 0.3771f, 0.3205f,
            0.2724f, 0.2316f, 0.1969f, 0.1674f, 0.1423f, 0.1210f, 0.1029f, 0.0874f,
            0.0743f, 0.0632f, 0.0538f, 0.0457f, 0.0389f, 0.0331f, 0.0281f, 0.0240f,
            0.0204f, 0.0174f, 0.0148f, 0.0126f, 0.0107f, 0.0091f, 0.0078f, 0.0066f,
            0.0056f, 0.0048f, 0.0041f, 0.0035f, 0.0029f, 0.0025f, 0.0021f, 0.0018f,
            0.0015f, 0.0013f, 0.0011f, 0.0009f // :)
        };
        public const float CHEESABLE_THRESHOLD = 34.375f;

        public List<DataVector>[] aimPerfDict;
        public List<DataVector>[] sortedAimPerfDict;
        public DataVectorAnalytics[] aimAnalyticsDict;

        public List<DataVector>[] tapPerfDict;
        public List<DataVector>[] sortedTapPerfDict;
        public DataVectorAnalytics[] tapAnalyticsDict;

        public float[] aimRatingDict;
        public float[] tapRatingDict;
        public float[] starRatingDict;

        private int NOTE_COUNT;

        public ChartPerformances(int noteCount, int sliderCount)
        {
            aimPerfDict = new List<DataVector>[7];
            sortedAimPerfDict = new List<DataVector>[7];
            tapPerfDict = new List<DataVector>[7];
            sortedTapPerfDict = new List<DataVector>[7];
            aimRatingDict = new float[7];
            tapRatingDict = new float[7];
            starRatingDict = new float[7];
            aimAnalyticsDict = new DataVectorAnalytics[7];
            tapAnalyticsDict = new DataVectorAnalytics[7];

            for (int i = 0; i < Utils.GAME_SPEED.Length; i++)
            {
                aimPerfDict[i] = new List<DataVector>(sliderCount);
                tapPerfDict[i] = new List<DataVector>(sliderCount);
            }
            NOTE_COUNT = noteCount;
        }

        public const float AIM_DIV = 175;
        public const float TAP_DIV = 225;
        public const float ACC_DIV = 190;
        public const float AIM_END = 900;
        public const float TAP_END = 9;
        public const float ACC_END = 1000;
        public const float MUL_END = 50;
        public const float MAX_DIST = 4f;

        public void CalculatePerformances(int speedIndex, List<Note> noteList)
        {
            var aimEndurance = 0f;
            var tapEndurance = 0f;
            for (int i = 0; i < NOTE_COUNT; i++) //Main Forward Loop
            {
                var currentNote = noteList[i];
                int noteCount = 0;
                float weightSum = 0f;
                var aimStrain = 0f;
                var tapStrain = 0f;
                for (int j = i - 1; j >= 0 && noteCount < 42 && Mathf.Abs(currentNote.position - noteList[j].position) <= MAX_DIST; j--)
                {
                    var prevNote = noteList[j];
                    var nextNote = noteList[j + 1];
                    if (prevNote.position >= nextNote.position) break;

                    var weight = weights[noteCount];
                    noteCount++;
                    weightSum += weight;

                    var deltaTime = nextNote.position - (prevNote.position + prevNote.length);

                    var lengthSum = prevNote.length;
                    var deltaSlideSum = Mathf.Abs(prevNote.pitchDelta);
                    if (deltaSlideSum <= CHEESABLE_THRESHOLD)
                        deltaSlideSum *= .45f;
                    var sliderCount = 0;
                    while (prevNote.isSlider)
                    {
                        if (j-- <= 0)
                            break;
                        prevNote = noteList[j];
                        nextNote = noteList[j + 1];
                        deltaTime = nextNote.position - (prevNote.position + prevNote.length);

                        var deltaSlide = Mathf.Abs(prevNote.pitchDelta);
                        if (deltaSlide == 0)
                            lengthSum += Mathf.Sqrt(prevNote.length);
                        else
                        {
                            lengthSum += prevNote.length;
                            if (deltaSlideSum <= CHEESABLE_THRESHOLD)
                                deltaSlide *= .35f;
                            else
                                sliderCount++;
                            deltaSlideSum += deltaSlide * (5.5f * sliderCount);
                        }

                    }

                    if (deltaSlideSum != 0)
                    {
                        //Acc Calc
                        aimStrain += ComputeStrain(CalcAccStrain(lengthSum, deltaSlideSum, weight)) / ACC_DIV;
                        aimEndurance += CalcAccEndurance(lengthSum, deltaSlideSum, weight);
                    }

                    //Aim Calc
                    deltaTime += lengthSum * .4f;
                    var aimDistance = Mathf.Abs(nextNote.pitchStart - prevNote.pitchEnd);
                    var noteMoved = aimDistance != 0 || deltaSlideSum != 0;

                    if (noteMoved)
                    {
                        aimStrain += ComputeStrain(CalcAimStrain(aimDistance, weight, deltaTime)) / AIM_DIV;
                        aimEndurance += CalcAimEndurance(aimDistance, weight, deltaTime);
                    }

                    //Tap Calc
                    var tapDelta = nextNote.position - prevNote.position;

                    tapStrain += ComputeStrain(CalcTapStrain(tapDelta, weight)) / TAP_DIV;
                    tapEndurance += CalcTapEndurance(tapDelta, weight);
                }

                if (i > 0)
                {
                    var aimThreshold = Mathf.Pow(aimStrain, 1.4f) * 3f;
                    var tapThreshold = Mathf.Pow(tapStrain, 1.8f) * 3f;
                    if (aimEndurance >= aimThreshold)
                        ComputeEnduranceDecay(ref aimEndurance, (aimEndurance - aimThreshold) / 60f);
                    if (tapEndurance >= tapThreshold)
                        ComputeEnduranceDecay(ref tapEndurance, (tapEndurance - tapThreshold) / 60f);
                }

                aimPerfDict[speedIndex].Add(new DataVector(currentNote.position, aimStrain, aimEndurance, weightSum));
                tapPerfDict[speedIndex].Add(new DataVector(currentNote.position, tapStrain, tapEndurance, weightSum));
            }
            sortedAimPerfDict[speedIndex] = aimPerfDict[speedIndex].OrderBy(x => x.performance).ToList();
            sortedTapPerfDict[speedIndex] = tapPerfDict[speedIndex].OrderBy(x => x.performance).ToList();
        }
        //public static bool IsSlider(float deltaTime) => !(Mathf.Round(deltaTime, 3) > 0);

        //https://www.desmos.com/calculator/e4kskdn8mu
        public static float ComputeStrain(float strain) => a * Mathf.Pow(strain + 1, -.012f * (float)Math.E) - a - (4f * strain) / a;
        private const float a = -50f;

        public static void ComputeEnduranceDecay(ref float endurance, float distanceFromLastNote)
        {
            endurance /= 1 + (.2f * distanceFromLastNote);
        }

        #region AIM
        public static float CalcAimStrain(float distance, float weight, float deltaTime)
        {
            var speed = Mathf.Pow(distance, .89f) / Mathf.Pow(deltaTime, 1.35f);
            return speed * weight;
        }

        public static float CalcAimEndurance(float distance, float weight, float deltaTime)
        {
            var speed = (Mathf.Pow(distance, .85f) / Mathf.Pow(deltaTime, 1.11f)) / (AIM_END * MUL_END);
            return speed * weight;
        }
        #endregion

        #region TAP
        public static float CalcTapStrain(float tapDelta, float weight)
        {
            return (16f / Mathf.Pow(tapDelta, 1.22f)) * weight;
        }   

        public static float CalcTapEndurance(float tapDelta, float weight)
        {
            return (0.8f / Mathf.Pow(tapDelta, 1.1f)) / (TAP_END * MUL_END) * weight;
        }
        #endregion

        #region ACC
        public static float CalcAccStrain(float lengthSum, float slideDelta, float weight)
        {
            var speed = slideDelta / Mathf.Pow(lengthSum, 1.18f);
            return speed * weight;
        }

        public static float CalcAccEndurance(float lengthSum, float slideDelta, float weight)
        {
            var speed = (slideDelta / Mathf.Pow(lengthSum, 1.08f)) / (ACC_END * MUL_END);
            return speed * weight;
        }
        #endregion

        public void CalculateAnalytics(int gamespeed)
        {
            aimAnalyticsDict[gamespeed] = new DataVectorAnalytics(aimPerfDict[gamespeed]);
            tapAnalyticsDict[gamespeed] = new DataVectorAnalytics(tapPerfDict[gamespeed]);
        }

        public const float BIAS = .75f;

        public void CalculateRatings(int gamespeed)
        {
            var aimRating = aimRatingDict[gamespeed] = aimAnalyticsDict[gamespeed].perfWeightedAverage + 0.01f;
            var tapRating = tapRatingDict[gamespeed] = tapAnalyticsDict[gamespeed].perfWeightedAverage + 0.01f;

            if (aimRating != 0 && tapRating != 0)
            {
                var totalRating = aimRating + tapRating;
                var aimPerc = aimRating / totalRating;
                var tapPerc = tapRating / totalRating;
                var aimWeight = (aimPerc + BIAS) * AIM_WEIGHT;
                var tapWeight = (tapPerc + BIAS) * TAP_WEIGHT;
                var totalWeight = aimWeight + tapWeight;
                starRatingDict[gamespeed] = ((aimRating * aimWeight) + (tapRating * tapWeight)) / totalWeight;
            }
            else
                starRatingDict[gamespeed] = 0f;
        }

        public float GetDynamicAimRating(float percent, float speed) => GetDynamicSkillRating(percent, speed, sortedAimPerfDict);
        public float GetDynamicTapRating(float percent, float speed) => GetDynamicSkillRating(percent, speed, sortedTapPerfDict);

        private float GetDynamicSkillRating(float percent, float speed, List<DataVector>[] skillRatingMatrix)
        {
            var index = (int)((speed - 0.5f) / .25f);

            if (skillRatingMatrix[index].Count <= 1 || percent <= 0)
                return 0;
            else if (speed % .25f == 0)
                return CalcSkillRating(percent, skillRatingMatrix[index]);

            var r1 = CalcSkillRating(percent, skillRatingMatrix[index]);
            var r2 = CalcSkillRating(percent, skillRatingMatrix[index + 1]);

            var minSpeed = Utils.GAME_SPEED[index];
            var maxSpeed = Utils.GAME_SPEED[index + 1];
            var by = (speed - minSpeed) / (maxSpeed - minSpeed);
            return Utils.Lerp(r1, r2, by);
        }

        public const float MAP = .01f;
        public const float MACC = .3f;

        private float CalcSkillRating(float percent, List<DataVector> skillRatingArray)
        {
            int maxRange;
            if (percent <= MACC)
                maxRange = (int)Mathf.Clamp(skillRatingArray.Count * (percent * (MAP / MACC)), 1, skillRatingArray.Count);
            else
                maxRange = (int)Mathf.Clamp(skillRatingArray.Count * ((percent - MACC) * ((1f-MAP)/(1f-MACC)) + MAP), 1, skillRatingArray.Count);

            var array = skillRatingArray.GetRange(0, maxRange);
            var analytics = new DataVectorAnalytics(array);
            return analytics.perfWeightedAverage + .01f;
        }

        public const float AIM_WEIGHT = 1.2f;
        public const float TAP_WEIGHT = 1.15f;

        public static readonly float[] HDWeights = { .12f, .09f };
        public static readonly float[] FLWeights = { .18f, .05f };

        public float GetDynamicDiffRating(float percent, float gamespeed, string[] modifiers = null)
        {
            var aimRating = GetDynamicAimRating(percent, gamespeed);
            var tapRating = GetDynamicTapRating(percent, gamespeed);

            if (aimRating == 0 && tapRating == 0) return 0f;

            if (modifiers != null)
            {
                var aimPow = 1f;
                var tapPow = 1f;
                if (modifiers.Contains("HD"))
                {
                    aimPow += HDWeights[0];
                    tapPow += HDWeights[1];
                }
                if (modifiers.Contains("FL"))
                {
                    aimPow += FLWeights[0];
                    tapPow += FLWeights[1];
                }

                aimRating = Mathf.Pow(aimRating + 1f, aimPow) - 1f;
                tapRating = Mathf.Pow(tapRating + 1f, tapPow) - 1f;
            }
            var totalRating = aimRating + tapRating;
            var aimPerc = aimRating / totalRating;
            var tapPerc = tapRating / totalRating;
            var aimWeight = (aimPerc + BIAS) * AIM_WEIGHT;
            var tapWeight = (tapPerc + BIAS) * TAP_WEIGHT;
            var totalWeight = aimWeight + tapWeight;

            return ((aimRating * aimWeight) + (tapRating * tapWeight)) / totalWeight;
        }

        public void Dispose()
        {
            aimPerfDict = null;
            sortedAimPerfDict = null;
            aimAnalyticsDict = null;
            aimRatingDict = null;
            tapPerfDict = null;
            sortedTapPerfDict = null;
            tapAnalyticsDict = null;
            tapRatingDict = null;
            starRatingDict = null;
        }

        public float GetDiffRating(float speed)
        {
            var index = (int)((speed - 0.5f) / .25f);
            if (speed % .25f == 0)
                return starRatingDict[index];

            var minSpeed = Utils.GAME_SPEED[index];
            var maxSpeed = Utils.GAME_SPEED[index + 1];
            var by = (speed - minSpeed) / (maxSpeed - minSpeed);
            return Utils.Lerp(starRatingDict[index], starRatingDict[index + 1], by);
        }

        public struct DataVector
        {
            public float performance;
            public float endurance;
            public float time;
            public float weight;

            public DataVector(float time, float performance, float endurance, float weight)
            {
                this.time = time;
                this.endurance = endurance;
                this.performance = performance;
                this.weight = weight;
            }
        }

        public struct DataVectorAnalytics
        {
            public float perfMax, perfSum, perfWeightedAverage;
            public float weightSum;

            public DataVectorAnalytics(List<DataVector> dataVectorList)
            {
                perfMax = perfSum = perfWeightedAverage = weightSum = 0;

                if (dataVectorList.Count <= 0) return;

                CalculateWeightSum(dataVectorList);
                CalculateData(dataVectorList);
            }

            public void CalculateWeightSum(List<DataVector> dataVectorList)
            {
                for(int i = 0; i < dataVectorList.Count; i++)
                    weightSum += dataVectorList[i].weight;
                if (weightSum < 300) weightSum = 300;
            }

            public void CalculateData(List<DataVector> dataVectorList)
            {
                for (int i = 0; i < dataVectorList.Count; i++)
                {
                    if (dataVectorList[i].performance > perfMax)
                        perfMax = dataVectorList[i].performance;

                    perfSum += (dataVectorList[i].performance + dataVectorList[i].endurance) * (dataVectorList[i].weight / weightSum);
                }
                perfWeightedAverage = perfSum;
            }
        }
        public static float BeatToSeconds2(float beat, float bpm) => 60f / bpm * beat;

    }

}
