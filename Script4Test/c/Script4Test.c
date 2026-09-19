/* Только тело функции становится блоками; шапка и includes переносятся как
   есть. Внутри — письменное подмножество NSG: объявления, присваивания,
   while/if, арифметика, приведение типа. */
int crunch(int n)
{
    int sieve[512];
    int primes = 0;
    int i = 0;
    while (i <= n)
    {
        sieve[i] = 0;
        i++;
    }
    i = 2;
    while (i <= n)
    {
        if (sieve[i] == 0)
        {
            primes += 1;
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
    return primes + bonus;
}
