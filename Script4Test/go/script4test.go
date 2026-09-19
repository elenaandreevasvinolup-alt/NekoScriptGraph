package script4test

// Только тело функции становится блоками; package и шапка переносятся как есть.
// Внутри — письменное подмножество NSG.
func Crunch(n int) int {
	primes := 0
	i := 2
	for i <= n {
		isPrime := 1
		j := 2
		for j*j <= i {
			if i%j == 0 {
				isPrime = 0
			}
			j += 1
		}
		primes += isPrime
		i += 1
	}
	mask := (1 << 8) - 1
	folded := (primes & mask) ^ (primes >> 3)
	bonus := -1
	if folded > 0 {
		bonus = folded
	}
	return primes + bonus
}
