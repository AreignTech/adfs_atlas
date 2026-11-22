ALTER TABLE "section_code" DROP CONSTRAINT "section_code_lab_id_lab_code_id_fk";
--> statement-breakpoint
DROP INDEX "sectionCode_labCode_uidx";--> statement-breakpoint
DROP INDEX "sectionCode_labId_idx";--> statement-breakpoint
WITH duplicate_sections AS (
  SELECT id,
         code,
         row_number() OVER (PARTITION BY lower(code) ORDER BY created_at, id) AS duplicate_number
  FROM "section_code"
)
UPDATE "section_code" AS section
SET code = section.code || '-' || duplicate_sections.duplicate_number
FROM duplicate_sections
WHERE section.id = duplicate_sections.id
  AND duplicate_sections.duplicate_number > 1;--> statement-breakpoint
CREATE UNIQUE INDEX "sectionCode_code_uidx" ON "section_code" USING btree ("code");--> statement-breakpoint
ALTER TABLE "section_code" DROP COLUMN "lab_id";