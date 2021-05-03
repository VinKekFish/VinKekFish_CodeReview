﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using static cryptoprime.BytesBuilderForPointers;

namespace vinkekfish
{
    /// <summary>Класс, генерирующий некриптостойкие значения на основе ожидания потоков.
    /// Обратите внимание, что на 1 байт сгенерированной информации рекомендуется принимать не более 1 бита случайной информации (а лучше - меньше)
    /// Пример использования см. в LightRandomGenerator_test01 и VinKekFish_k1_base_20210419_keyGeneration.EnterToBackgroundCycle</summary>
    public unsafe class LightRandomGenerator: IDisposable
    {
        public    volatile bool    ended  = false;
        protected readonly Thread rthread = null;
        protected readonly Thread wthread = null;

        /// <summary>Если <see langword="true"/>, то вызывает Thread.Sleep(doSleepR) на каждой итерации извлечения байта, в противном случае - только по необходимости. Рекомендуется true</summary>
        public    volatile bool   doSleepR = true;

        protected volatile ushort curCNT  = 0;
        protected volatile ushort lastCNT = 0;
        public LightRandomGenerator(int CountToGenerate)
        {
            this.CountToGenerate = CountToGenerate;

            var allocator  = new AllocHGlobal_AllocatorForUnsafeMemory();
            GeneratedBytes = allocator.AllocMemory(CountToGenerate);

            wthread = new Thread
            (
                delegate()
                {
                    try
                    {
                        while (!ended)
                        {
                            curCNT++;

                            // Временный останов
                            if (GeneratedCount >= CountToGenerate)
                            lock (this)
                            {
                                Monitor.Wait(this);
                            }
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref isEnded);
                        lock (this)
                        {
                            Monitor.PulseAll(this);
                        }
                    }
                }
            );

            rthread = new Thread
            (
                delegate()
                {
                    try
                    {
                        while (!ended)
                        {
                            if (doSleepR)
                                Thread.Sleep(0);

                            while (lastCNT == curCNT)
                                Thread.Sleep(0);

                            lastCNT = curCNT;

                            lock (this)
                            {
                                if (GeneratedCount < CountToGenerate)
                                {
                                    // На всякий случай делаем xor между младшим и старшим байтом, чтобы все биты были учтены
                                    // Не такая уж хорошая статистика получается по младшим байтам, как могло бы быть
                                    GeneratedBytes.array[(GeneratedCount + StartOfGenerated) % CountToGenerate] = (byte) (curCNT ^ (curCNT >> 8));
                                    // GeneratedBytes.array[(GeneratedCount + StartOfGenerated) % CountToGenerate] = (byte) curCNT;
                                    GeneratedCount++;
                                }
                                else
                                {
                                    wthread.Priority = ThreadPriority.Lowest;
                                    Monitor.PulseAll(this);
                                    Monitor.Wait(this, 1000);
                                }
                            }
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref isEnded);
                        lock (this)
                        {
                            Monitor.PulseAll(this);
                        }
                    }
                }
            );

            wthread.Priority = ThreadPriority.Lowest;
            rthread.Priority = ThreadPriority.Lowest;

            wthread.Start();
            rthread.Start();
        }

        /// <summary>Брать байты можно и прямо из массива после WaitForGenerator. После взятия вызвать ResetGeneratedBytes</summary>
        public    readonly   Record GeneratedBytes   = null;
        protected readonly   int    CountToGenerate  = 0;
        protected volatile   int    GeneratedCount   = 0;
        protected volatile   int    StartOfGenerated = 0;
        protected volatile   int    isEnded          = 2;

        /// <summary>Сбрасывает все сгенерированные байты без полезного использования. Это стоит вызвать, если GeneratedBytes использованы напрямую</summary>
        public virtual void ResetGeneratedBytes()
        {
            StartOfGenerated = 0;
            GeneratedCount   = 0;

            wthread.Priority = ThreadPriority.Normal;
            lock (this)
                Monitor.PulseAll(this);
        }

        /// <summary>Получает из генератора псевдослучайные некриптостойкие байты. Брать байты можно и прямо из массива GeneratedBytes</summary>
        /// <param name="result">Некриптостойкий результат. result != <see langword="null"/>, result.Length must be less or equal CountToGenerate</param>
        public virtual void GetRandomBytes(byte[] result)
        {
            if (result.Length > CountToGenerate)
                throw new ArgumentOutOfRangeException("LightRandomGenerator.GetRandomBytes: result.Length > CountToGenerate");
            
            WaitForGenerator(result.LongLength);
            if (ended)
                throw new Exception("LightRandomGenerator.GetRandomBytes: LightRandomGenerator is end of work");

            lock (this)
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = GeneratedBytes.array[StartOfGenerated];

                StartOfGenerated++;
                GeneratedCount--;
                if (StartOfGenerated >= CountToGenerate)
                    StartOfGenerated = 0;
            }

            wthread.Priority = ThreadPriority.Normal;
            lock (this)
                Monitor.PulseAll(this);
        }

        public virtual void WaitForGenerator(long mustGenerated = 0)
        {
            if (mustGenerated <= 0)
                mustGenerated = CountToGenerate;

            lock (this)
            {
                while (GeneratedCount < mustGenerated && !ended)
                {
                    Monitor.PulseAll(this);
                    Monitor.Wait(this, 1000);
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Очищает объект</summary>
        /// <param name="disposing"><see langword="true"/> во всех случаях, кроме вызова из деструктора</param>
        public virtual void Dispose(bool disposing)
        {
            ended = true;

            lock (this)
            {
                while (isEnded > 0)
                {
                    Monitor.PulseAll(this);
                    Monitor.Wait(this, 100);
                }

                GeneratedBytes.Dispose();
            }
        }

        ~LightRandomGenerator()
        {
            Dispose(false);
        }
    }
}
