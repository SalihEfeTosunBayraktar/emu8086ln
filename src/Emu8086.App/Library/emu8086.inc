; emu8086.inc - helper macros and procedures (emu8086ln implementation)
; Compatible names with the classic emu8086 library, written from scratch.
;
; Macros (expand in place):
;   PUTC    char            - print one character
;   GOTOXY  col, row        - move the cursor
;   PRINT   'text'          - print text
;   PRINTN  'text'          - print text and a new line
;   CURSOROFF / CURSORON    - hide / show the cursor
;
; Procedures (declare with DEFINE_xxx once, then CALL xxx):
;   DEFINE_SCAN_NUM       -> SCAN_NUM       reads a signed number into CX
;   DEFINE_PRINT_STRING   -> PRINT_STRING   prints ASCIIZ string at DS:SI
;   DEFINE_PTHIS          -> PTHIS          prints ASCIIZ string following the CALL
;   DEFINE_GET_STRING     -> GET_STRING     reads a string into DS:DI (DX = buffer size)
;   DEFINE_CLEAR_SCREEN   -> CLEAR_SCREEN   clears the screen, cursor to 0,0
;   DEFINE_PRINT_NUM      -> PRINT_NUM      prints signed AX (needs DEFINE_PRINT_NUM_UNS)
;   DEFINE_PRINT_NUM_UNS  -> PRINT_NUM_UNS  prints unsigned AX

PUTC MACRO char
    PUSH AX
    MOV AL, char
    MOV AH, 0Eh
    INT 10h
    POP AX
ENDM

GOTOXY MACRO col, row
    PUSH AX
    PUSH BX
    PUSH DX
    MOV AH, 02h
    MOV DH, row
    MOV DL, col
    MOV BH, 0
    INT 10h
    POP DX
    POP BX
    POP AX
ENDM

PRINT MACRO text
    LOCAL text_data, text_start, text_loop, text_end
    PUSH AX
    PUSH SI
    JMP text_start
text_data DB text, 0
text_start:
    LEA SI, text_data
text_loop:
    MOV AL, CS:[SI]
    CMP AL, 0
    JE text_end
    MOV AH, 0Eh
    INT 10h
    INC SI
    JMP text_loop
text_end:
    POP SI
    POP AX
ENDM

PRINTN MACRO text
    PRINT text
    PUTC 13
    PUTC 10
ENDM

CURSOROFF MACRO
    PUSH AX
    PUSH CX
    MOV AH, 1
    MOV CH, 20h
    MOV CL, 0
    INT 10h
    POP CX
    POP AX
ENDM

CURSORON MACRO
    PUSH AX
    PUSH CX
    MOV AH, 1
    MOV CH, 6
    MOV CL, 7
    INT 10h
    POP CX
    POP AX
ENDM

DEFINE_SCAN_NUM MACRO
    LOCAL skip_proc, read_key, not_minus, not_backspace, finish, positive, negative_flag
    JMP skip_proc
SCAN_NUM PROC NEAR
    PUSH AX
    PUSH BX
    PUSH DX
    XOR CX, CX
    MOV BYTE PTR CS:negative_flag, 0
read_key:
    MOV AH, 0
    INT 16h
    CMP AL, 13
    JE finish
    CMP AL, 8
    JNE not_backspace
    MOV AX, CX
    XOR DX, DX
    MOV BX, 10
    DIV BX
    MOV CX, AX
    PUTC 8
    PUTC ' '
    PUTC 8
    JMP read_key
not_backspace:
    CMP AL, '-'
    JNE not_minus
    CMP CX, 0
    JNE read_key
    MOV BYTE PTR CS:negative_flag, 1
    PUTC '-'
    JMP read_key
not_minus:
    CMP AL, '0'
    JB read_key
    CMP AL, '9'
    JA read_key
    MOV BL, AL
    MOV AX, CX
    MOV DX, 10
    MUL DX
    JC read_key
    MOV DL, BL
    SUB DL, '0'
    MOV DH, 0
    ADD AX, DX
    JC read_key
    MOV CX, AX
    PUTC BL
    JMP read_key
finish:
    CMP BYTE PTR CS:negative_flag, 0
    JE positive
    NEG CX
positive:
    POP DX
    POP BX
    POP AX
    RET
negative_flag DB 0
SCAN_NUM ENDP
skip_proc:
ENDM

DEFINE_PRINT_STRING MACRO
    LOCAL skip_proc, next_char, done
    JMP skip_proc
PRINT_STRING PROC NEAR
    PUSH AX
    PUSH SI
next_char:
    MOV AL, [SI]
    CMP AL, 0
    JE done
    MOV AH, 0Eh
    INT 10h
    INC SI
    JMP next_char
done:
    POP SI
    POP AX
    RET
PRINT_STRING ENDP
skip_proc:
ENDM

DEFINE_PTHIS MACRO
    LOCAL skip_proc, next_char, done
    JMP skip_proc
PTHIS PROC NEAR
    PUSH BP
    MOV BP, SP
    PUSH AX
    PUSH SI
    MOV SI, [BP+2]
next_char:
    MOV AL, CS:[SI]
    INC SI
    CMP AL, 0
    JE done
    MOV AH, 0Eh
    INT 10h
    JMP next_char
done:
    MOV [BP+2], SI
    POP SI
    POP AX
    POP BP
    RET
PTHIS ENDP
skip_proc:
ENDM

DEFINE_GET_STRING MACRO
    LOCAL skip_proc, get_key, store_char, finish
    JMP skip_proc
GET_STRING PROC NEAR
    PUSH AX
    PUSH CX
    PUSH DX
    PUSH DI
    XOR CX, CX
    CMP DX, 1
    JBE finish
    DEC DX
get_key:
    MOV AH, 0
    INT 16h
    CMP AL, 13
    JE finish
    CMP AL, 8
    JNE store_char
    JCXZ get_key
    DEC CX
    DEC DI
    PUTC 8
    PUTC ' '
    PUTC 8
    JMP get_key
store_char:
    CMP CX, DX
    JAE get_key
    MOV [DI], AL
    INC DI
    INC CX
    MOV AH, 0Eh
    INT 10h
    JMP get_key
finish:
    MOV BYTE PTR [DI], 0
    POP DI
    POP DX
    POP CX
    POP AX
    RET
GET_STRING ENDP
skip_proc:
ENDM

DEFINE_CLEAR_SCREEN MACRO
    LOCAL skip_proc
    JMP skip_proc
CLEAR_SCREEN PROC NEAR
    PUSH AX
    PUSH BX
    PUSH CX
    PUSH DX
    MOV AX, 0600h
    MOV BH, 07h
    MOV CX, 0
    MOV DX, 184Fh
    INT 10h
    MOV AH, 02h
    MOV BH, 0
    MOV DX, 0
    INT 10h
    POP DX
    POP CX
    POP BX
    POP AX
    RET
CLEAR_SCREEN ENDP
skip_proc:
ENDM

DEFINE_PRINT_NUM_UNS MACRO
    LOCAL skip_proc, divide, output
    JMP skip_proc
PRINT_NUM_UNS PROC NEAR
    PUSH AX
    PUSH BX
    PUSH CX
    PUSH DX
    MOV BX, 10
    XOR CX, CX
divide:
    XOR DX, DX
    DIV BX
    PUSH DX
    INC CX
    CMP AX, 0
    JNE divide
output:
    POP AX
    ADD AL, '0'
    MOV AH, 0Eh
    INT 10h
    LOOP output
    POP DX
    POP CX
    POP BX
    POP AX
    RET
PRINT_NUM_UNS ENDP
skip_proc:
ENDM

DEFINE_PRINT_NUM MACRO
    LOCAL skip_proc, positive
    JMP skip_proc
PRINT_NUM PROC NEAR
    PUSH AX
    CMP AX, 0
    JGE positive
    PUTC '-'
    NEG AX
positive:
    CALL PRINT_NUM_UNS
    POP AX
    RET
PRINT_NUM ENDP
skip_proc:
ENDM
