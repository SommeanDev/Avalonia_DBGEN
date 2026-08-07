-- ============================================================
-- MASTER PROCEDURE
-- Schema : {{schema}}
-- Table  : {{table_name}}
-- PK     : {{pk_col}} {{pk_type}}
-- ============================================================

-- DROP PROCEDURE pr_{{table_name}}(...);

CREATE OR REPLACE PROCEDURE pr_{{table_name}}(
    INOUT pi_{{pk_col}} {{pk_type}},
    IN    pi_opflag character varying,
    IN    pi_operation_date date,
    IN    {{context_param}} integer,
    {{business_parameters}}
    IN    pi_enter_user_id integer,
    IN    pi_enter_desc character varying,
    IN    pi_pass_flag character varying,
    INOUT pi_opid character varying,
    INOUT pi_error character varying
)
LANGUAGE plpgsql
AS $procedure$
DECLARE
    lv_{{pk_col}} {{pk_type}};
    lv_opid character varying(10);
    lv_error character varying(4000);
    lv_newval character varying(4000);
    lv_oldval character varying(4000);
    lv_cnt integer;
    lv_opdate date := COALESCE(pi_operation_date, CURRENT_DATE);
    child_flag character varying(1) := 'N';
    cnt record;
    lv_err_no character varying(4000);
    lv_err_at_line character varying(4000);
    lv_err_desc character varying(4000);
BEGIN

    lv_opid := fn_getopid(pi_opflag);

    IF pi_opflag = 'N' THEN
        lv_{{pk_col}} := fn_getnextval('{{table_name}}');

        INSERT INTO {{table_name}} (
            {{pk_col}},
            server_sys_date,
            operation_date,
            {{context_col}},
            operation_id,
{{insert_business_columns}}
            active_flag,
            delete_flag,
            enter_user_id,
            enter_desc,
            pass_flag,
            pass_user_id,
            pass_desc
        )
        VALUES (
            lv_{{pk_col}},
            CURRENT_TIMESTAMP,
            lv_opdate,
            {{context_param}},
            lv_opid,
{{insert_business_values}}
            'Y',
            'N',
            pi_enter_user_id,
            pi_enter_desc,
            pi_pass_flag,
            CASE WHEN pi_pass_flag = 'Y' THEN pi_enter_user_id ELSE NULL END,
            CASE WHEN pi_pass_flag = 'Y' THEN pi_enter_desc ELSE NULL END
        );

        CALL pr_operationdet(
            NULL, CURRENT_DATE, NULL, NULL, lv_{{pk_col}}::integer,
            '{{table_label}} MAINTENANCE',
            'N', 'N', UPPER('{{table_name}}'), 'ABBR', NULL, 'N',
            pi_enter_user_id, pi_enter_desc, lv_opid, lv_error
        );

        IF lv_error = 'success' THEN
            pi_{{pk_col}} := lv_{{pk_col}};
            pi_opid := lv_opid;
        ELSE
            pi_opid := NULL;
            pi_error := lv_error;
        END IF;

    ELSIF pi_opflag = 'M' THEN
        DROP TABLE IF EXISTS g_{{table_name}};

        CREATE TEMP TABLE g_{{table_name}} AS
        SELECT * FROM {{table_name}} WHERE 1 = 2;

        INSERT INTO g_{{table_name}} (
            {{pk_col}},
            server_sys_date,
            operation_date,
            {{context_col}},
            operation_id,
{{temp_business_columns}}
            active_flag,
            delete_flag,
            enter_user_id,
            enter_desc,
            delete_user_id,
            delete_desc,
            pass_flag,
            pass_user_id,
            pass_desc
        )
        VALUES (
            pi_{{pk_col}},
            CURRENT_TIMESTAMP,
            lv_opdate,
            {{context_param}},
            lv_opid,
{{temp_business_values}}
            'Y',
            'N',
            pi_enter_user_id,
            pi_enter_desc,
            NULL,
            NULL,
            pi_pass_flag,
            CASE WHEN pi_pass_flag = 'Y' THEN pi_enter_user_id ELSE NULL END,
            CASE WHEN pi_pass_flag = 'Y' THEN pi_enter_desc ELSE NULL END
        );

        pi_opid := lv_opid;

        CALL pr_operationdet(
            NULL, CURRENT_DATE, NULL, NULL, pi_{{pk_col}}::integer,
            '{{table_label}} MAINTENANCE',
            'M', 'N', UPPER('{{table_name}}'), 'LOAN', NULL, 'N',
            pi_enter_user_id, pi_enter_desc, lv_opid, lv_error
        );

        IF lv_error = 'success' THEN
            FOR cnt IN (
                SELECT t.column_name, t.table_name, k.temp_table,
                       k.primary_key_name, t.data_type
                FROM information_schema.columns t
                INNER JOIN table_key_master k ON t.table_name = k.table_name
                WHERE t.table_schema = current_schema()
                  AND t.table_name = '{{table_name}}'
                  AND t.column_name IN (
                      SELECT c.field_name
                      FROM caption_details c
                      WHERE c.table_name = t.table_name
                        AND c.modify_allow = 'Y'
                        AND c.block_modify = 'N'
                  )
            ) LOOP
                IF cnt.data_type NOT IN ('bytea', 'text') THEN
                    EXECUTE 'SELECT ' || cnt.column_name || '::character varying FROM ' || cnt.temp_table ||
        ' WHERE {{pk_col}} = ' || pi_{{pk_col}} INTO lv_newval;

                    EXECUTE 'SELECT ' || cnt.column_name || '::character varying FROM ' || cnt.table_name ||
        ' WHERE {{pk_col}} = ' || pi_{{pk_col}} INTO lv_oldval;

                    IF COALESCE(TRIM(lv_newval), ' ') <> COALESCE(TRIM(lv_oldval), ' ') THEN
                        INSERT INTO modification_log (
                            mod_log_det_id, transaction_date, branch_mst_id,
                            table_name, field_name, old_value, new_value,
                            primary_key_field, primary_key_id, operation_id,
                            enter_user_id, enter_desc, accepted_rejected_flag
                        ) VALUES (
                            fn_getnextval('modification_log'), lv_opdate,
                            {{context_param}},
                            '{{table_name}}', cnt.column_name, lv_oldval, lv_newval,
                            '{{pk_col}}', pi_{{pk_col}}, lv_opid,
                            pi_enter_user_id, pi_enter_desc, 'P'
                        );
                    END IF;
                END IF;
            END LOOP;

            SELECT COUNT(1) INTO lv_cnt
            FROM modification_log
            WHERE to_char(transaction_date, 'DD/MM/YYYY') = to_char(lv_opdate, 'DD/MM/YYYY')
              AND operation_id = lv_opid;

            IF COALESCE(lv_cnt, 0) <= 0 THEN
                pi_opid := NULL;
                pi_error := 'No Change in Data. Data is Identical';
                RETURN;
            END IF;
        END IF;

        UPDATE {{table_name}}
        SET
{{update_business_assignments}}
            operation_id = lv_opid,
            operation_date = lv_opdate,
            last_edit_user_id = pi_enter_user_id,
            last_edit_desc = pi_enter_desc,
            pass_flag = pi_pass_flag,
            pass_user_id = CASE WHEN pi_pass_flag = 'Y' THEN pi_enter_user_id ELSE NULL END,
            pass_desc = CASE WHEN pi_pass_flag = 'Y' THEN pi_enter_desc ELSE NULL END
        WHERE {{pk_col}} = pi_{{pk_col}};

        pi_opid := lv_opid;

    ELSIF pi_opflag = 'D' THEN
        child_flag := fn_child_record_found('{{table_name}}', pi_{{pk_col}}::integer);

        IF child_flag = 'N' THEN
            UPDATE {{table_name}}
            SET operation_id = lv_opid,
                operation_date = lv_opdate,
                active_flag = 'N',
                delete_flag = 'Y',
                pass_flag = COALESCE(pi_pass_flag, 'Y'),
                delete_user_id = pi_enter_user_id,
                delete_desc = pi_enter_desc
            WHERE {{pk_col}} = pi_{{pk_col}};

            CALL pr_operationdet(
                NULL, CURRENT_DATE, NULL, NULL, pi_{{pk_col}}::integer,
                '{{table_label}} MAINTENANCE',
                'D', 'N', UPPER('{{table_name}}'), 'LOAN', NULL, 'N',
                pi_enter_user_id, pi_enter_desc, lv_opid, lv_error
            );

            IF lv_error = 'success' THEN
                pi_opid := lv_opid;
            ELSE
                pi_error := lv_error;
            END IF;
        ELSE
            pi_error := 'Child record found, record not deleted.';
        END IF;
    END IF;

    pi_error := CASE WHEN pi_error IS NULL THEN 'success' ELSE pi_error END;

EXCEPTION
    WHEN OTHERS THEN
        GET STACKED DIAGNOSTICS
            lv_err_no = RETURNED_SQLSTATE,
            lv_err_desc = PG_EXCEPTION_DETAIL,
            lv_err_at_line = PG_EXCEPTION_CONTEXT;

        CALL pr_insert_errlog(
            {{context_param}},
            'pr_{{table_name}}',
            lv_err_no,
            fn_geterror_codedesc(lv_err_no),
            pi_enter_user_id,
            NULL,
            CURRENT_DATE,
            SUBSTR(lv_err_at_line, STRPOS(lv_err_at_line, ')') + 2)
        );

        pi_error := 'error,' || lv_err_no || ',' || fn_geterror_codedesc(lv_err_no);
        RAISE NOTICE 'ERROR: %', SQLERRM;
END;
$procedure$;
