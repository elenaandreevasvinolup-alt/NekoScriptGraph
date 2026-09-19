using System.Collections.Generic;

// Только тело метода становится блоками; using и шапка класса переносятся
// как есть. Внутри — письменное подмножество NSG. Дженерики — в текстовых
// слотах типа, поэтому переводятся блоком, а не сырым фрагментом.
public class Script4Test
{
    public int Crunch(int n)
    {
        List<int> sieve = new List<int>();
        Dictionary<string, int> tally = new Dictionary<string, int>();
        int primes = 0;
        int i = 0;
        while (i <= n)
        {
            sieve.Add(0);
            i++;
        }
        i = 2;
        while (i <= n)
        {
            if (sieve[i] == 0)
            {
                primes += 1;
                tally["p" + primes] = i;
                int j = i * i;
                while (j <= n)
                {
                    sieve[j] = 1;
                    j += i;
                }
            }
            i++;
        }
        int mask = (1 << 8) - 1;
        int folded = (primes & mask) ^ (primes >> 3);
        int bonus = folded > 0 ? folded : -1;
        return primes + bonus + tally.Count;
    }
}
