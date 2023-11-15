DECLARE @datum DATE;
SET @datum = '2020-12-21';

SELECT
	Season.SeasonId,
	Season.DateFrom,
	Season.DateUntil,
	CASE
		WHEN YEAR(Season.DateFrom) = YEAR(Season.DateUntil)
		THEN
			CASE
				WHEN
					(
						-- Season.DateFrom (2000) < NOW (2000)
						MONTH(Season.DateFrom) < MONTH(CAST(@datum AS SQL_DATE))
						OR (
							MONTH(Season.DateFrom) = MONTH(CAST(@datum AS SQL_DATE))
							AND
							DAYOFMONTH(Season.DateFrom) <= DAYOFMONTH(CAST(@datum AS SQL_DATE))
						)
					)
					AND
					(
						-- NOW (2000) < Season.DateUntil (2000)
						MONTH(CAST(@datum AS SQL_DATE)) < MONTH(Season.DateUntil)
						OR (
							MONTH(CAST(@datum AS SQL_DATE)) = MONTH(Season.DateUntil)
							AND
							DAYOFMONTH(CAST(@datum AS SQL_DATE)) <= DAYOFMONTH(Season.DateUntil)
						)
					)
				THEN TRUE
				ELSE FALSE
			END
		ELSE
			CASE
				-- ( Season.DateFrom (2000) < NOW (2000) < 31/12) OR (1/1/2000 < NOW (2000) < Season.DateUntil (2000) )
				WHEN
					-- Season.DateFrom (2000) < NOW (2000) < 31/12/2000)
					(
						(
							-- Season.DateFrom (2000) <= NOW (2000)
							MONTH(Season.DateFrom) < MONTH(CAST(@datum AS SQL_DATE))
							OR (
								MONTH(Season.DateFrom) = MONTH(CAST(@datum AS SQL_DATE))
								AND
								DAYOFMONTH(Season.DateFrom) <= DAYOFMONTH(CAST(@datum AS SQL_DATE))
							)
						)
						AND
						(
							-- NOW (2000) <= 31/12)
							MONTH(CAST(@datum AS SQL_DATE)) < 12
							OR (
								MONTH(CAST(@datum AS SQL_DATE)) = 12
								AND
								DAYOFMONTH(CAST(@datum AS SQL_DATE)) <= 31
							)
						)
					)
					OR
					-- 1/1/2000 < NOW (2000) < Season.DateUntil (2000)
					(
						(
							-- 1/1/2000 < NOW (2000)
							1 < MONTH(CAST(@datum AS SQL_DATE))
							OR (
								1 = MONTH(CAST(@datum AS SQL_DATE))
								AND
								1 <= DAYOFMONTH(CAST(@datum AS SQL_DATE))
							)
						)
						AND
						(
							-- NOW (2000) < Season.DateUntil (2000)
							MONTH(CAST(@datum AS SQL_DATE)) < MONTH(Season.DateUntil)
							OR (
								MONTH(CAST(@datum AS SQL_DATE)) = MONTH(Season.DateUntil)
								AND
								DAYOFMONTH(CAST(@datum AS SQL_DATE)) <= DAYOFMONTH(Season.DateUntil)
							)
						)
					)
				THEN TRUE
				ELSE FALSE
				END
		END
		AS CurrentSeason
FROM Season
	WHERE Season.Active = TRUE